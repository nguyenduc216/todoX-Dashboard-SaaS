# RVID-IMAGE-PROMPT-PLACEHOLDER-FALSE-POSITIVE.1

Root cause: parser and ScenePromptMetadata used case-insensitive TODO substring
matching, which also matched TodoX. The parser used it for effective image source,
fallback selection and warnings. Metadata used it for usability and edited prompt
normalization. Editor, import preview and generation selection reuse these paths;
activation persistence itself contains no placeholder detector.

Fix: parser delegates to the shared metadata detector. TODO must have a left word
boundary and be followed by optional whitespace and a colon or closing bracket.
TodoX and embedded brand names remain usable. Existing explicit placeholder
markers, empty checks, fallback order and other schema checks remain unchanged.
Prompt content is not rewritten.

Changed files:
- TodoX.Web/Services/VideoRender/ScenePromptMetadata.cs
- TodoX.Web/Services/VideoRender/TodoXVideoPromptParser.cs
- TodoX.Web.Tests/ImagePromptPlaceholderRegressionTests.cs
- This history file.

Verification (from TodoX.Web unless stated otherwise):
- dotnet test ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ImagePromptPlaceholderRegressionTests|FullyQualifiedName~TodoXVideoPromptParserTests|FullyQualifiedName~ScenePromptMetadataTests|FullyQualifiedName~RVideoP3B1|FullyQualifiedName~RVideoP3B2" --verbosity quiet
  PASS: 57 tests, zero failures.
- dotnet format whitespace TodoX.Web.csproj --no-restore --verify-no-changes --include Services/VideoRender/ScenePromptMetadata.cs Services/VideoRender/TodoXVideoPromptParser.cs
  PASS.
- dotnet format whitespace ../TodoX.Web.Tests/TodoX.Web.Tests.csproj --no-restore --verify-no-changes --include ../TodoX.Web.Tests/ImagePromptPlaceholderRegressionTests.cs
  PASS.
- git diff --check: PASS.
- From repository root: dotnet build TodoX.Web/TodoX.Web.csproj -c Release --verbosity quiet
  PASS: zero errors, 46 existing warnings.
- From repository root: dotnet publish TodoX.Web/TodoX.Web.csproj -c Release -o "D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\artifacts\publish" --verbosity quiet
  Local publish only; artifact is excluded from Git.

Protected systems unchanged: render engine, TTS/audio, billing, scene history,
final merge, character reference, database schema and Agent 195 instructions.
Existing user changes were retained. No migration or production deployment.
The reported generation was not read from the database; regression tests cover
the demonstrated TodoX substring trigger, markers, fallback and other schema errors.
