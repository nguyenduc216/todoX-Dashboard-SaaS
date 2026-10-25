using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed partial class SceneVideoOperationDraftPostgresTests
{
    [SupersedePostgresTheory]
    [InlineData(862, 5)]
    [InlineData(864, 7)]
    public async Task Project195ImageRerenderUsesRealDraftJobAndPreservesSelectionOnFailure(long sceneId, int sceneIndex)
    {
        var cs = Environment.GetEnvironmentVariable("TODOX_SUPERSEDE_TEST_DB")!;
        ValidateIsolatedConnection(cs);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN('pg_catalog','information_schema');"));
        var owned = false;
        try
        {
            await conn.ExecuteAsync(ImageRerenderFixtureSql);
            owned = true;
            var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var oldImage = Guid.NewGuid();
            await conn.ExecuteAsync("""
                INSERT INTO video_render.video_project_scenes(id,project_id,tenant_id,selected_image_version_id,status,static_image_url)
                VALUES(@sceneId,195,@tenant,@oldImage,'image_ready','/selected-v1.png');
                INSERT INTO video_render.scene_image_versions(id,project_id,scene_id,tenant_id,version_number,logical_request_id,status,is_selected,public_url)
                VALUES(@oldImage,195,@sceneId,@tenant,1,'selected-v1','completed',true,'/selected-v1.png');
                """, new { sceneId, tenant, oldImage });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = tenant.ToString() }).Build();
            var factory = new TodoXConnectionFactory(config);
            var versions = new SceneMediaVersioningService(factory, new TenantContext(factory, config), config);
            SceneImageVersionCreateRequest Request(string logical, Guid? job = null) => new(195, sceneId, user, null, job,
                logical, "correct three-hand image", "compiled prompt", "motion", null, new { sceneIndex }, new { }, new { billingOperationId = user });
            SceneImageJobDraft Draft(string logical = "rerender") => new("render_scene_image", user, null, 195, "video-195", "Rerender scene", null, null, 100,
                System.Text.Json.JsonSerializer.Serialize(new { projectId = 195, sceneId, sceneIndex, imageVersionId = Guid.Empty,
                    logicalRequestId = logical, billingReferenceId = user, prompt = "compiled prompt" }), "{}", "[]");
            var selectedSnapshot = await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(v)::text FROM video_render.scene_image_versions v WHERE id=@oldImage;", new { oldImage });
            // Exact old Legacy bug: an operation UUID is not a persisted job UUID.
            var fk = await Assert.ThrowsAsync<PostgresException>(() => versions.CreateQueuedImageVersionAsync(Request("legacy-invalid", Guid.NewGuid())));
            Assert.Equal("23503", fk.SqlState);
            Assert.Equal("fk_scene_image_render_job", fk.ConstraintName);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_image_versions;"));
            await conn.ExecuteAsync("""
                CREATE FUNCTION video_render.fixture_image_job_first() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NOT EXISTS(SELECT 1 FROM render.render_jobs j WHERE j.tenant_id=NEW.tenant_id AND j.status='draft'
                    AND j.project_id=NEW.project_id AND j.input_json->>'imageVersionId'=NEW.id::text) THEN
                    RAISE EXCEPTION 'Draft job must already exist before the image version';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER fixture_job_first BEFORE INSERT ON video_render.scene_image_versions
                  FOR EACH ROW EXECUTE FUNCTION video_render.fixture_image_job_first();
                CREATE FUNCTION render.fixture_image_activation_bound() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                  IF NEW.status='queued' AND NOT EXISTS(SELECT 1 FROM video_render.scene_image_versions v
                    WHERE v.render_job_id=NEW.id AND v.tenant_id=NEW.tenant_id AND v.project_id=NEW.project_id
                      AND v.id::text=NEW.input_json->>'imageVersionId') THEN
                    RAISE EXCEPTION 'Activation must follow the real FK binding';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER fixture_bind_first BEFORE UPDATE ON render.render_jobs
                  FOR EACH ROW EXECUTE FUNCTION render.fixture_image_activation_bound();
                """);
            await Assert.ThrowsAsync<InvalidOperationException>(() => versions.CreateQueuedImageVersionWithJobAsync(
                Request("wrong-project"), Draft() with { ProjectId = 999 }));

            // Fail activation after a valid job/version/bind: the entire attempt must roll back.
            await conn.ExecuteAsync("ALTER TABLE render.render_jobs ADD CONSTRAINT injected_activation_failure CHECK(status<>'queued');");
            await Assert.ThrowsAsync<PostgresException>(() => versions.CreateQueuedImageVersionWithJobAsync(Request("activation-fails"), Draft("activation-fails")));
            Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_jobs;"));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_image_versions;"));
            Assert.Equal(oldImage, await conn.ExecuteScalarAsync<Guid>("SELECT selected_image_version_id FROM video_render.video_project_scenes WHERE id=@sceneId;", new { sceneId }));
            Assert.Equal(selectedSnapshot, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(v)::text FROM video_render.scene_image_versions v WHERE id=@oldImage;", new { oldImage }));
            var otherConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:TodoXSaaS"] = cs, ["TodoX:TenantId"] = Guid.NewGuid().ToString() }).Build();
            var otherVersions = new SceneMediaVersioningService(new TodoXConnectionFactory(otherConfig),
                new TenantContext(new TodoXConnectionFactory(otherConfig), otherConfig), otherConfig);
            await Assert.ThrowsAsync<InvalidOperationException>(() => otherVersions.CreateQueuedImageVersionWithJobAsync(Request("other-tenant"), Draft()));
            await conn.ExecuteAsync("ALTER TABLE render.render_jobs DROP CONSTRAINT injected_activation_failure;");

            var concurrent = await Task.WhenAll(
                versions.CreateQueuedImageVersionWithJobAsync(Request("rerender"), Draft()),
                versions.CreateQueuedImageVersionWithJobAsync(Request("rerender"), Draft()));
            Assert.Single(concurrent, outcome => !outcome.AlreadyActive);
            var created = concurrent.Single(outcome => !outcome.AlreadyActive);
            Assert.All(concurrent, outcome => Assert.Equal(created.RenderJobId, outcome.RenderJobId));
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_jobs WHERE status='queued';"));
            Assert.Equal(2, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_image_versions;"));
            Assert.True(await conn.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM video_render.scene_image_versions v JOIN render.render_jobs j ON j.id=v.render_job_id
                WHERE v.id=@id AND j.id=@job AND j.tenant_id=v.tenant_id AND j.project_id=v.project_id
                  AND j.input_json->>'imageVersionId'=v.id::text AND j.input_json->>'billingReferenceId'=@billing
                  AND v.version_number=2 AND v.status='queued' AND j.status='queued' AND NOT v.is_selected
                  AND j.provider_code='configured_image_router' AND j.model_code='scene_image_default'
                  AND j.point_cost_estimate=0 AND j.point_status='not_required');
                """, new { id = created.Version!.Id, job = created.RenderJobId, billing = user.ToString() }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => versions.CreateQueuedImageVersionWithJobAsync(Request("second-click"), Draft()));
            Assert.Equal(selectedSnapshot, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(v)::text FROM video_render.scene_image_versions v WHERE id=@oldImage;", new { oldImage }));
            await conn.ExecuteAsync("UPDATE video_render.scene_image_versions SET status='failed' WHERE id=@id; UPDATE render.render_jobs SET status='failed' WHERE id=@job;",
                new { id = created.Version.Id, job = created.RenderJobId });
            async Task<bool> CompetingClickAsync(string logical)
            {
                try { await versions.CreateQueuedImageVersionWithJobAsync(Request(logical), Draft(logical)); return true; }
                catch (InvalidOperationException ex) when (ex.Message == "SCENE_IMAGE_ACTIVE") { return false; }
            }
            var competing = await Task.WhenAll(CompetingClickAsync("click-a"), CompetingClickAsync("click-b"));
            Assert.Single(competing, result => result);
            Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM render.render_jobs WHERE status='queued';"));
            Assert.Equal(3, await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM video_render.scene_image_versions;"));
            Assert.Equal(selectedSnapshot, await conn.ExecuteScalarAsync<string>("SELECT to_jsonb(v)::text FROM video_render.scene_image_versions v WHERE id=@oldImage;", new { oldImage }));
        }
        finally
        {
            if (owned) await conn.ExecuteAsync("ROLLBACK; DROP SCHEMA video_render CASCADE; DROP SCHEMA render CASCADE;");
        }
    }

    private const string ImageRerenderFixtureSql = """
        CREATE SCHEMA render; CREATE SCHEMA video_render;
        CREATE TABLE render.render_jobs(id uuid PRIMARY KEY,tenant_id uuid,user_id uuid,customer_id uuid,project_id bigint,
          job_type text,status text,priority integer,input_json jsonb,prompt_json jsonb,reference_json jsonb,log_code text,
          error_code text,error_message text,attempts integer,max_attempts integer,created_at timestamptz,updated_at timestamptz,
          provider_code text,model_code text,point_cost_estimate numeric,point_status text);
        CREATE TABLE video_render.video_project_scenes(id bigint PRIMARY KEY,project_id bigint,tenant_id uuid,
          selected_image_version_id uuid,status text,static_image_url text);
        CREATE TABLE video_render.scene_image_versions(id uuid PRIMARY KEY,project_id bigint,scene_id bigint,tenant_id uuid,
          customer_id uuid,created_by uuid,version_number integer,logical_request_id text,render_job_id uuid,
          image_prompt_snapshot text,compiled_image_prompt_snapshot text,video_prompt_snapshot text,negative_prompt_snapshot text,
          scene_snapshot_json jsonb,reference_snapshot_json jsonb,render_config_json jsonb,storage_key text,status text,
          created_at timestamptz,updated_at timestamptz,is_selected boolean DEFAULT false,public_url text,source_file_path text,
          result_media_id uuid,provider_code text,actual_model text,provider_capability_id bigint,provider_task_id text,
          billing_logical_request_id text,estimated_usd numeric,actual_usd numeric,charged_points numeric DEFAULT 0,
          refunded_points numeric DEFAULT 0,error_message text,
          CONSTRAINT fk_scene_image_render_job FOREIGN KEY(render_job_id) REFERENCES render.render_jobs(id),
          UNIQUE(tenant_id,logical_request_id),UNIQUE(tenant_id,scene_id,version_number));
        """;
}
