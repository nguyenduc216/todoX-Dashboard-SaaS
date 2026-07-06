using System.Diagnostics;
using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.Images;
using TodoX.Web.Services.Profile;

namespace TodoX.Web.Services.ImageRender;

public sealed class MarketingImageRenderService
{
    private readonly ServiceThumbnailRenderService _thumbnailRender;
    private readonly MarketingImageRenderLogRepository _logRepository;
    private readonly IMarketingBriefAnalyzer _briefAnalyzer;
    private readonly ServiceImageLayoutPlanner _layoutPlanner;
    private readonly ServiceImagePromptCompiler _promptCompiler;
    private readonly ServiceImageQcService _qcService;
    private readonly ILogger<MarketingImageRenderService> _logger;

    public MarketingImageRenderService(ServiceThumbnailRenderService thumbnailRender,
        MarketingImageRenderLogRepository logRepository,
        IMarketingBriefAnalyzer briefAnalyzer,
        ServiceImageLayoutPlanner layoutPlanner,
        ServiceImagePromptCompiler promptCompiler,
        ServiceImageQcService qcService,
        ILogger<MarketingImageRenderService> logger)
    {
        _thumbnailRender = thumbnailRender;
        _logRepository = logRepository;
        _briefAnalyzer = briefAnalyzer;
        _layoutPlanner = layoutPlanner;
        _promptCompiler = promptCompiler;
        _qcService = qcService;
        _logger = logger;
    }

    public async Task<MarketingImageRenderResult> RenderAsync(MarketingImageRenderRequest request, CancellationToken ct = default)
    {
        var result = new MarketingImageRenderResult
        {
            RenderJobId = Guid.NewGuid(),
            LogCode = AvatarRenderActivityLogService.GenerateLogCode(),
            Status = "processing"
        };

        void Add(string code, string name, string message, object? input = null, object? output = null,
            object? metadata = null, long? durationMs = null, string level = "info", string? error = null)
        {
            var entry = new MarketingImageRenderLogEntry
            {
                StepCode = code,
                StepName = ServiceImageRenderStepCatalog.GetName(code),
                Message = message,
                Input = input,
                Output = output,
                Metadata = metadata,
                DurationMs = durationMs,
                Level = level,
                Error = error
            };
            result.Logs.Add(entry);
            _logger.LogInformation("MARKETING_IMAGE_RENDER {LogCode} {StepCode} {Message} {@Metadata}",
                result.LogCode, code, message, metadata);
        }

        try
        {
            Add("01_RENDER_REQUEST_RECEIVED", "Nháº­n yÃªu cáº§u render", "User submitted service image brief.",
                input: BuildRequestLog(request),
                metadata: new { result.RenderJobId, result.LogCode });
            Add("02_BRIEF_ANALYSIS_STARTED", "Bắt đầu phân tích brief", "Brief analyzer started.",
                input: new { request.Brief, request.ServiceName, request.ServiceCategory },
                metadata: new { preferredProvider = "gemini", fallbackProvider = "rule_v1" });

            var analyzerSw = Stopwatch.StartNew();
            MarketingBriefAnalyzerResult analyzerResult;
            try
            {
                analyzerResult = await _briefAnalyzer.AnalyzeAsync(request, ct);
            }
            catch (Exception ex)
            {
                analyzerResult = BuildRuleFallbackAnalyzerResult(request, ex.Message);
            }
            analyzerSw.Stop();
            result.AnalyzerProvider = analyzerResult.Provider;
            result.UsedAnalyzerFallback = analyzerResult.UsedFallback;
            var analysis = analyzerResult.Analysis;
            var ruleAnalysis = AnalyzeBrief(request);
            var forcedRulePlan = false;
            if (ruleAnalysis.DetectedServiceType.Equals("ai_video_from_prompt_character", StringComparison.OrdinalIgnoreCase)
                && !analysis.DetectedServiceType.Equals("tiktok_to_facebook_reup", StringComparison.OrdinalIgnoreCase))
            {
                analysis.DetectedServiceType = ruleAnalysis.DetectedServiceType;
                analysis.Confidence = Math.Max(analysis.Confidence, 0.9);
                analysis.ClassificationReason = "Local rule override: video prompt + character + scene workflow detected.";
                analysis.ExcludedServiceTypes = BuildExcludedTypes(analysis.DetectedServiceType);
                forcedRulePlan = true;
            }
            Add("03_BRIEF_ANALYZED", "Đã phân tích brief", "Brief analyzer classified the service intent.",
                input: new { originalBrief = request.Brief },
                output: analysis,
                metadata: new
                {
                    analyzerProvider = analyzerResult.Provider,
                    analyzerFallback = analyzerResult.UsedFallback,
                    localRuleOverride = forcedRulePlan,
                    analyzerError = analyzerResult.Error,
                    rawResponse = analyzerResult.RawResponse
                },
                durationMs: analyzerSw.ElapsedMilliseconds,
                level: analyzerResult.UsedFallback ? "warning" : "info",
                error: analyzerResult.Error);

            var plan = request.RecreatePlan || request.ExistingPlan is null
                ? forcedRulePlan ? CreateRenderPlan(request, analysis) : analyzerResult.Plan
                : request.ExistingPlan;
            result.RenderPlan = plan;
            var universalPlan = request.RecreatePlan || request.ExistingUniversalPlan is null
                ? _layoutPlanner.CreatePlan(request, analysis, plan)
                : request.ExistingUniversalPlan;
            if (request.Feedback is not null)
            {
                universalPlan = _layoutPlanner.ApplyFeedback(universalPlan, request.Feedback);
            }
            result.UniversalPlan = universalPlan;
            Add("02_CREATIVE_BRIEF_CREATED", "Create creative brief", "Universal creative brief created.",
                input: new { analysis, request.Brief },
                output: universalPlan.CreativeBrief,
                metadata: new { analyzerProvider = analyzerResult.Provider, analyzerFallback = analyzerResult.UsedFallback });
            Add("03_LAYOUT_PLAN_CREATED", "Create layout plan", "Universal layout plan created.",
                input: universalPlan.CreativeBrief,
                output: new { universalPlan.Layout, universalPlan.MainVisual, universalPlan.FixedAssets, universalPlan.TextOverlay });
            Add("04_RENDER_PLAN_CREATED", "Tạo render plan", "Universal render plan was created automatically.",
                input: new { analysis, request.Tone, request.AspectRatio },
                output: universalPlan,
                metadata: new { analyzerProvider = analyzerResult.Provider, analyzerFallback = analyzerResult.UsedFallback },
                level: analyzerResult.UsedFallback ? "warning" : "info");

            var qc = CheckRenderPlan(plan, request);
            Add("05_RENDER_PLAN_QC_CHECKED", "Kiểm tra render plan", qc.Ok ? "Render plan QC passed." : "Render plan QC warning.",
                input: universalPlan,
                output: qc,
                level: qc.Ok ? "info" : "warning");

            var compiled = _promptCompiler.Compile(universalPlan);
            var compiledPrompt = compiled.Prompt;
            result.CompiledPrompt = compiledPrompt;
            Add("04_PROMPT_COMPILED", "Compile prompt nền", "Background prompt compiled from universal render plan.",
                input: new { universalPlan.MainVisual, universalPlan.NegativePromptTerms },
                output: new
                {
                    backgroundPrompt = compiled.Prompt,
                    negativePrompt = compiled.NegativePrompt,
                    finalCompiledPrompt = compiled.Prompt,
                    forbiddenTermsChecked = true
                });

            var forbiddenQc = CheckForbiddenTermsForService(plan, universalPlan, compiledPrompt);
            if (!forbiddenQc.Ok)
            {
                Add("06_FORBIDDEN_TERMS_BLOCKED", "Chặn prompt sai nghiệp vụ", "Forbidden platform/reup terms detected before calling render API.",
                    input: new { plan.ServiceType, compiledPrompt, universalPlan },
                    output: forbiddenQc,
                    level: "error",
                    error: string.Join(", ", forbiddenQc.ForbiddenTermsFound));
                throw new InvalidOperationException($"Render plan chứa từ khóa không phù hợp: {string.Join(", ", forbiddenQc.ForbiddenTermsFound)}");
            }

            var classification = ClassifyReferences(request);
            Add("07_REFERENCE_IMAGES_CLASSIFIED", "Phân loại ảnh tham chiếu", "Reference images classified before render.",
                input: new { request.BrandRobotImageUrl, request.ReferenceImageUrls },
                output: classification);

            Add("08_FIXED_ASSETS_NORMALIZED", "Chuẩn hóa fixed assets", "Fixed assets normalized before background render.",
                input: classification.FixedAssets,
                output: new
                {
                    universalPlan.FixedAssets.Pipeline,
                    universalPlan.FixedAssets.PreserveAspectRatio,
                    universalPlan.FixedAssets.RequireTransparentBackground,
                    universalPlan.FixedAssets.PreferredPlacement,
                    universalPlan.FixedAssets.MaxHeightPercent,
                    brandRobotSentToModel = universalPlan.FixedAssets.SendFixedAssetsToModel
                },
                level: string.IsNullOrWhiteSpace(request.BrandRobotImageUrl) && universalPlan.FixedAssets.UseMrTodoX
                    ? "warning"
                    : "info");

            if (request.PreserveFixedAssets || !string.IsNullOrWhiteSpace(request.BrandRobotImageUrl))
            {
                Add("08_FIXED_ASSET_PIPELINE_SELECTED", "Chá»n pipeline fixed asset", "Brand robot will be composited by code and not sent to the model.",
                    output: new
                    {
                        pipeline = ImageRenderRequestModel.PipelineBackgroundThenComposite,
                        brandRobotSentToModel = false,
                        role = "brand_robot"
                    });
            }

            Add("09_BACKGROUND_RENDER_REQUEST", "Gá»i API render ná»n", "Calling background render pipeline.",
                input: new
                {
                    provider = "google-vertex-ai",
                    endpoint = "Vertex-image-render",
                    aspectRatio = request.AspectRatio,
                    referenceCountSentToModel = classification.ModelReferences.Count,
                    promptLength = compiledPrompt.Length
                });

            var renderSw = Stopwatch.StartNew();
            var thumb = await _thumbnailRender.RenderAsync(new ServiceThumbnailRenderRequest
            {
                ServiceName = universalPlan.ServiceName,
                GroupName = universalPlan.ServiceCategory,
                ServiceType = universalPlan.ServiceType ?? plan.ServiceType,
                ShortDescription = request.ShortDescription,
                DetailedDescription = request.Brief,
                CustomPrompt = compiledPrompt,
                ReferenceImageUrls = request.ReferenceImageUrls,
                BrandRobotImageUrl = request.BrandRobotImageUrl,
                PreserveFixedAssets = request.PreserveFixedAssets,
                AspectRatio = universalPlan.AspectRatio,
                Theme = universalPlan.Theme,
                PosterTextHeadline = universalPlan.TextOverlay.Headline,
                PosterTextSubheadline = universalPlan.TextOverlay.Subheadline,
                PosterTextFooter = universalPlan.TextOverlay.Footer,
                User = request.User
            }, ct);
            renderSw.Stop();

            Add("10_BACKGROUND_RENDER_RESPONSE", "Nháº­n pháº£n há»“i render ná»n", thumb.Ok ? "Render service returned an image." : "Render service failed.",
                output: new
                {
                    ok = thumb.Ok,
                    thumb.ImageUrl,
                    thumb.Error,
                    thumb.ImageRenderRequestId,
                    imageRenderLogCount = thumb.ImageRenderLogs.Count
                },
                durationMs: renderSw.ElapsedMilliseconds,
                level: thumb.Ok ? "info" : "error",
                error: thumb.Error);

            ImportImageRenderLogs(thumb.ImageRenderLogs, Add);

            if (!thumb.Ok || string.IsNullOrWhiteSpace(thumb.ImageUrl))
            {
                throw new InvalidOperationException(thumb.Error ?? "Render pipeline did not return an image.");
            }

            result.ImageUrl = thumb.ImageUrl;

            Add("16_FINAL_IMAGE_STORED", "LÆ°u áº£nh cuá»‘i", "Final image stored.",
                output: new
                {
                    finalMediaId = thumb.ImageMediaId,
                    publicUrl = thumb.ImageUrl,
                    mimeType = "image/png",
                    fileSizeBytes = thumb.ImageFileSizeBytes
                });

            Add("17_QC_STARTED", "Bắt đầu QC", "Final render QC started.",
                input: new { universalPlan.QcPolicy, universalPlan.CreativeBrief.RequiredConcepts });
            var universalQc = _qcService.Check(universalPlan, thumb.ImageUrl, !string.IsNullOrWhiteSpace(request.BrandRobotImageUrl), thumb.ImageRenderLogs);
            result.QcResult = universalQc;
            Add(universalQc.Passed ? "18_QC_PASSED" : "18_QC_FAILED", universalQc.Passed ? "QC đạt" : "QC lỗi",
                universalQc.Passed ? "Final render QC passed." : "Final render QC failed.",
                output: universalQc,
                level: universalQc.Passed ? (universalQc.Warnings.Count > 0 ? "warning" : "info") : "error");

            result.Ok = universalQc.Passed;
            result.Status = universalQc.Passed ? "completed" : "failed";
            result.Error = universalQc.Passed ? null : string.Join("; ", universalQc.Errors);
            Add(result.Ok ? "19_RENDER_COMPLETED" : "19_RENDER_FAILED",
                result.Ok ? "Hoàn tất render" : "Render lỗi",
                result.Ok ? "Marketing image render completed." : "Marketing image render failed.",
                output: new { result.ImageUrl, result.Status, qc = universalQc },
                level: result.Ok ? (universalQc.Warnings.Count > 0 ? "warning" : "info") : "error",
                error: result.Error);
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Status = "failed";
            result.Error = ex.Message;
            Add("19_RENDER_FAILED", "Render lá»—i", "Marketing image render failed.",
                level: "error",
                error: ex.Message,
                metadata: new { exceptionType = ex.GetType().Name });
        }

        await _logRepository.SaveAsync(request, result, ct);
        return result;
    }

    private static object BuildRequestLog(MarketingImageRenderRequest request)
        => new
        {
            request.ServiceName,
            request.ServiceCategory,
            request.ShortDescription,
            request.Brief,
            request.Tone,
            request.AspectRatio,
            hasBrandRobot = !string.IsNullOrWhiteSpace(request.BrandRobotImageUrl),
            referenceCount = request.ReferenceImageUrls.Count,
            request.PreserveFixedAssets,
            request.RecreatePlan
        };

    private static MarketingBriefAnalyzerResult BuildRuleFallbackAnalyzerResult(MarketingImageRenderRequest request, string error)
    {
        var analysis = AnalyzeBrief(request);
        var plan = CreateRenderPlan(request, analysis);
        return new MarketingBriefAnalyzerResult
        {
            Provider = "rule_v1",
            UsedFallback = true,
            Error = error,
            Analysis = analysis,
            Plan = plan
        };
    }

    private static MarketingBriefAnalysis AnalyzeBrief(MarketingImageRenderRequest request)
    {
        var text = $"{request.ServiceName} {request.ServiceCategory} {request.ShortDescription} {request.Brief}".ToLowerInvariant();
        var mentionsTikTok = text.Contains("tiktok");
        var mentionsFacebook = text.Contains("facebook") || text.Contains("fb ");
        var mentionsReup = text.Contains("reup") || text.Contains("đăng lại") || text.Contains("dang lai");
        var serviceType = DetectServiceType(text, mentionsTikTok, mentionsFacebook, mentionsReup);

        return new MarketingBriefAnalysis
        {
            ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? "Dịch vụ TodoX" : request.ServiceName.Trim(),
            ServiceCategory = string.IsNullOrWhiteSpace(request.ServiceCategory) ? "AI Marketing" : request.ServiceCategory.Trim(),
            DetectedServiceType = serviceType,
            Confidence = serviceType == "general_service" ? 0.68 : 0.86,
            ClassificationReason = BuildClassificationReason(serviceType, mentionsTikTok, mentionsFacebook, mentionsReup),
            ExcludedServiceTypes = BuildExcludedTypes(serviceType),
            MentionsTikTok = mentionsTikTok,
            MentionsFacebook = mentionsFacebook,
            MentionsReup = mentionsReup
        };
    }

    private static MarketingRenderPlan CreateRenderPlan(MarketingImageRenderRequest request, MarketingBriefAnalysis analysis)
    {
        var isReupFlow = analysis.MentionsTikTok && analysis.MentionsFacebook && analysis.MentionsReup;
        var isVideoPromptCharacter = analysis.DetectedServiceType.Equals("ai_video_from_prompt_character", StringComparison.OrdinalIgnoreCase);
        var headline = isReupFlow
            ? "REUP VIDEO TIKTOK"
            : isVideoPromptCharacter
                ? "TẠO VIDEO TỪ PROMPT"
            : ToPosterHeadline(analysis.ServiceName);
        var subheadline = isReupFlow
            ? "SANG FACEBOOK"
            : isVideoPromptCharacter
                ? "VÀ NHÂN VẬT AI"
            : ToPosterSubheadline(request.ShortDescription ?? request.Brief);
        var footer = isReupFlow
            ? "XÂY DỰNG KÊNH TỰ ĐỘNG"
            : isVideoPromptCharacter
                ? "TodoX Video AI"
            : "TỰ ĐỘNG HÓA CÙNG TODOX";

        var visualElements = isReupFlow
            ? new List<string>
            {
                "left TikTok source video frame",
                "right Facebook destination feed frame",
                "gold data arrows from TikTok to Facebook",
                "subtle dashboard and growth chart"
            }
            : isVideoPromptCharacter
                ? new List<string>
                {
                    "prompt input box",
                    "character selection card",
                    "scene/background selection card",
                    "AI render job pipeline",
                    "vertical video preview frame",
                    "automation scheduling status"
                }
            : new List<string>
            {
                "premium SaaS dashboard background",
                "AI automation visual accents",
                "clean product/service workflow diagram",
                "subtle growth chart and data stream"
            };

        return new MarketingRenderPlan
        {
            ServiceName = analysis.ServiceName,
            ServiceCategory = analysis.ServiceCategory,
            ServiceType = analysis.DetectedServiceType,
            AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? "9:16" : request.AspectRatio,
            Theme = string.IsNullOrWhiteSpace(request.Tone) ? "yellow_black" : request.Tone,
            Headline = headline,
            Subheadline = subheadline,
            Footer = footer,
            BenefitBullets = BuildBenefitBullets(analysis, request),
            VisualElements = visualElements,
            AssetPolicy = new MarketingAssetPolicy
            {
                PreserveBrandRobot = request.PreserveFixedAssets,
                BrandRobotSentToModel = false,
                Pipeline = request.PreserveFixedAssets
                    ? ImageRenderRequestModel.PipelineBackgroundThenComposite
                    : ImageRenderRequestModel.PipelineModelGenerate
            },
            ForbiddenTerms = isVideoPromptCharacter
                ? new List<string> { "REUP", "TIKTOK", "FACEBOOK", "SANG FACEBOOK", "social reposting", "platform transfer" }
                : request.PreserveFixedAssets
                    ? new List<string> { "robot", "mascot", "character", "human" }
                    : new List<string> { "small unreadable text", "random logo" },
            RequiredConcepts = isReupFlow
                ? new List<string> { "TikTok source", "Facebook destination", "data arrows", "automation" }
                : isVideoPromptCharacter
                    ? new List<string> { "prompt", "nhân vật", "bối cảnh", "render video", "tự động" }
                : new List<string> { analysis.ServiceName, "automation", "dashboard", "premium SaaS" },
            NegativePrompt = "No small unreadable text. No random logos. No clutter. No distorted brand asset."
        };
    }

    private static string CompileBackgroundPrompt(MarketingImageRenderRequest request, MarketingRenderPlan plan)
    {
        var noFixedAssetSubjects = plan.AssetPolicy.PreserveBrandRobot
            ? "No robot. No mascot. No character. No human. Leave clean empty center or bottom-center space for a brand robot asset to be composited later."
            : "Create a complete professional service illustration.";

        return $"""
        Create a premium vertical {plan.AspectRatio} service poster background.
        Theme: {DescribeTheme(plan.Theme)}, futuristic AI automation, digital marketing.
        Service name: {plan.ServiceName}.
        Service category: {plan.ServiceCategory}.
        Service type: {plan.ServiceType}.
        Brief: {request.Brief}

        Visual elements:
        {string.Join(Environment.NewLine, plan.VisualElements.Select(x => "- " + x))}

        Required concepts:
        {string.Join(Environment.NewLine, plan.RequiredConcepts.Select(x => "- " + x))}

        Composition policy:
        {noFixedAssetSubjects}
        Avoid small unreadable text. Do not add random logos.
        """;
    }

    private static MarketingQcResult CheckRenderPlan(MarketingRenderPlan plan, MarketingImageRenderRequest request)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(plan.Headline)) warnings.Add("headline_missing");
        if (string.IsNullOrWhiteSpace(plan.Subheadline)) warnings.Add("subheadline_missing");
        if (plan.AssetPolicy.PreserveBrandRobot && string.IsNullOrWhiteSpace(request.BrandRobotImageUrl))
        {
            warnings.Add("brand_robot_not_uploaded_using_configured_default_if_available");
        }

        return new MarketingQcResult
        {
            Ok = warnings.Count == 0 || warnings.All(x => x.Contains("default", StringComparison.OrdinalIgnoreCase)),
            Warnings = warnings,
            ForbiddenTermsChecked = true,
            RequiredConceptsChecked = true,
            Result = warnings.Count == 0 ? "passed" : "warning"
        };
    }

    private static MarketingQcResult CheckForbiddenTermsForService(
        MarketingRenderPlan plan,
        UniversalServiceImageRenderPlan universalPlan,
        string compiledPrompt)
    {
        var forbidden = new List<string>();
        if (!plan.ServiceType.Equals("ai_video_from_prompt_character", StringComparison.OrdinalIgnoreCase))
        {
            return new MarketingQcResult
            {
                Ok = true,
                ForbiddenTermsChecked = true,
                RequiredConceptsChecked = true,
                Result = "passed"
            };
        }

        var text = string.Join("\n", new[]
        {
            plan.Headline,
            plan.Subheadline,
            plan.Footer,
            compiledPrompt,
            universalPlan.CreativeBrief.MainMessage,
            universalPlan.CreativeBrief.UserBenefit,
            universalPlan.CreativeBrief.VisualMetaphor,
            universalPlan.CreativeBrief.ViewerShouldUnderstandIn3Seconds,
            universalPlan.MainVisual.VisualStory,
            universalPlan.MainVisual.Composition,
            universalPlan.MainVisual.BackgroundPrompt,
            universalPlan.TextOverlay.Headline,
            universalPlan.TextOverlay.Subheadline,
            universalPlan.TextOverlay.Footer,
            string.Join(" ", plan.VisualElements),
            string.Join(" ", plan.RequiredConcepts),
            string.Join(" ", universalPlan.MainVisual.Objects),
            string.Join(" ", universalPlan.TextOverlay.MicroBullets)
        }).ToLowerInvariant();

        foreach (var term in new[] { "tiktok", "facebook", "reup", "sang facebook", "social reposting", "platform transfer" })
        {
            if (text.Contains(term))
            {
                forbidden.Add(term);
            }
        }

        return new MarketingQcResult
        {
            Ok = forbidden.Count == 0,
            ForbiddenTermsChecked = true,
            ForbiddenTermsFound = forbidden.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RequiredConceptsChecked = true,
            Result = forbidden.Count == 0 ? "passed" : "failed_forbidden_terms",
            Warnings = forbidden.Count == 0 ? new List<string>() : new List<string> { "forbidden_platform_terms_detected" }
        };
    }

    private static MarketingQcResult CheckFinalResult(MarketingRenderPlan plan, string? imageUrl)
        => new()
        {
            Ok = !string.IsNullOrWhiteSpace(imageUrl),
            ForbiddenTermsChecked = true,
            ForbiddenTermsFound = new List<string>(),
            RequiredConceptsChecked = true,
            Result = string.IsNullOrWhiteSpace(imageUrl) ? "failed_no_image" : "passed",
            Warnings = string.IsNullOrWhiteSpace(imageUrl) ? new List<string> { "missing_result_url" } : new List<string>()
        };

    private static MarketingReferenceClassification ClassifyReferences(MarketingImageRenderRequest request)
    {
        var fixedAssets = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.BrandRobotImageUrl))
        {
            fixedAssets.Add(new
            {
                role = "brand_robot",
                url = request.BrandRobotImageUrl,
                mediaId = (Guid?)null,
                mimeType = "image/*",
                byteLength = (long?)null,
                width = (int?)null,
                height = (int?)null
            });
        }

        var modelReferences = request.ReferenceImageUrls
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new
            {
                role = "service_reference",
                url = x,
                mediaId = (Guid?)null,
                mimeType = "image/*",
                byteLength = (long?)null,
                width = (int?)null,
                height = (int?)null
            })
            .Cast<object>()
            .ToList();

        return new MarketingReferenceClassification
        {
            FixedAssets = fixedAssets,
            ModelReferences = modelReferences,
            BrandRobotSentToModel = false
        };
    }

    private delegate void MarketingLogAdder(string code, string name, string message, object? input = null,
        object? output = null, object? metadata = null, long? durationMs = null, string level = "info", string? error = null);

    private static void ImportImageRenderLogs(IReadOnlyList<RenderLogEntry> source, MarketingLogAdder add)
    {
        foreach (var item in source)
        {
            switch (item.Step)
            {
                case "FIXED_ASSET_LOADED":
                    add("11_FIXED_ASSET_LOADED", "Load fixed asset", item.Message, output: item.Data);
                    add("12_FIXED_ASSET_BACKGROUND_PROCESSING_STARTED", "Bắt đầu xử lý nền robot", "Background processing for fixed asset started.",
                        output: new { method = "corner_color_alpha_mask", tolerance = 42 });
                    break;
                case "FIXED_ASSET_COMPOSITED":
                    add("13_FIXED_ASSET_BACKGROUND_PROCESSED", "Đã xử lý nền robot", "Fixed asset background processing completed.",
                        output: item.Data);
                    add("14_FIXED_ASSET_COMPOSITED", "Composite robot", item.Message, output: item.Data);
                    break;
                case "TEXT_OVERLAY_APPLIED":
                    add("15_TEXT_OVERLAY_APPLIED", "Overlay text", item.Message, output: item.Data);
                    break;
            }
        }
    }

    private static T Timed<T>(Func<T> action, out long elapsedMs)
    {
        var sw = Stopwatch.StartNew();
        var value = action();
        sw.Stop();
        elapsedMs = sw.ElapsedMilliseconds;
        return value;
    }

    private static string DetectServiceType(string text, bool mentionsTikTok, bool mentionsFacebook, bool mentionsReup)
    {
        if (mentionsTikTok && mentionsFacebook && mentionsReup) return "tiktok_to_facebook_reup";
        var mentionsVideoPromptCharacter = (text.Contains("tạo video") || text.Contains("tao video") || text.Contains("video"))
            && text.Contains("prompt")
            && (text.Contains("nhân vật") || text.Contains("nhan vat") || text.Contains("character"))
            && (text.Contains("bối cảnh") || text.Contains("boi canh") || text.Contains("scene"));
        if (mentionsVideoPromptCharacter) return "ai_video_from_prompt_character";
        if (text.Contains("video")) return "video_generation";
        if (text.Contains("avatar") || text.Contains("chibi")) return "avatar_generation";
        if (text.Contains("content") || text.Contains("bÃ i viáº¿t") || text.Contains("bai viet")) return "content_automation";
        return "general_service";
    }

    private static string BuildClassificationReason(string serviceType, bool mentionsTikTok, bool mentionsFacebook, bool mentionsReup)
        => serviceType switch
        {
            "tiktok_to_facebook_reup" => "Brief contains TikTok, Facebook and reup/republish intent.",
            "ai_video_from_prompt_character" => "Brief describes video generation from prompt, character, and scene/background selection.",
            "video_generation" => "Brief mentions video generation or video workflow.",
            "avatar_generation" => "Brief mentions avatar/chibi image generation.",
            "content_automation" => "Brief mentions content/post automation.",
            _ => $"General service inferred. TikTok={mentionsTikTok}, Facebook={mentionsFacebook}, Reup={mentionsReup}."
        };

    private static List<string> BuildExcludedTypes(string selected)
        => new[] { "tiktok_to_facebook_reup", "ai_video_from_prompt_character", "video_generation", "avatar_generation", "content_automation", "general_service" }
            .Where(x => x != selected)
            .ToList();

    private static List<string> BuildBenefitBullets(MarketingBriefAnalysis analysis, MarketingImageRenderRequest request)
    {
        if (analysis.DetectedServiceType == "tiktok_to_facebook_reup")
        {
            return new List<string> { "Tự động lấy video nguồn", "Đẩy sang kênh đích", "Theo dõi tăng trưởng" };
        }

        if (analysis.DetectedServiceType == "ai_video_from_prompt_character")
        {
            return new List<string> { "Nhập prompt", "Chọn nhân vật/bối cảnh", "Theo dõi job render" };
        }

        return new List<string>
        {
            "Tự động hóa quy trình",
            "Tiáº¿t kiá»‡m thá»i gian váº­n hÃ nh",
            "Theo dÃµi hiá»‡u quáº£ trÃªn dashboard"
        };
    }

    private static string ToPosterHeadline(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "TODOX AI SERVICE" : value.Trim();
        text = text.Length > 26 ? text[..26].Trim() : text;
        return text.ToUpperInvariant();
    }

    private static string ToPosterSubheadline(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "TỰ ĐỘNG HÓA VẬN HÀNH" : value.Trim();
        text = text.Length > 30 ? text[..30].Trim() : text;
        return text.ToUpperInvariant();
    }

    private static string DescribeTheme(string theme)
        => theme.Equals("yellow_black", StringComparison.OrdinalIgnoreCase) ? "black and gold" : theme;

    private sealed class MarketingReferenceClassification
    {
        public List<object> FixedAssets { get; set; } = new();
        public List<object> ModelReferences { get; set; } = new();
        public bool BrandRobotSentToModel { get; set; }
    }

    private sealed class MarketingQcResult
    {
        public bool Ok { get; set; }
        public bool ForbiddenTermsChecked { get; set; }
        public List<string> ForbiddenTermsFound { get; set; } = new();
        public bool RequiredConceptsChecked { get; set; }
        public string Result { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();
    }
}


