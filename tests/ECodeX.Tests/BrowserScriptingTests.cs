using ECodeX.Core.IPC.V2;
using ECodeX.Core.Models;
using ECodeX.Core.Services;
using FluentAssertions;
using Xunit;

namespace ECodeX.Tests;

/// <summary>
/// Browser scripting P0 smoke contract. Marked as Windows-only integration so CI can
/// opt into the WebView2-backed version without mixing it into fast unit tests.
/// </summary>
public class BrowserScriptingIntegrationTests
{
    private const string Category = "WindowsOnlyIntegration";

    [Fact]
    [Trait("Category", Category)]
    public void P0Smoke_LocalPageSnapshotClickFillEvalContract()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "browser-p0.html");
        File.Exists(fixturePath).Should().BeTrue("the browser integration fixture should be copied to test output");

        var actionRequests = new List<BrowserScriptingActionRequest>();
        var service = new BrowserScriptingService(
            () =>
            [
                new BrowserScriptingSurfaceDescriptor(
                    WorkspaceId: "workspace-1",
                    WorkspaceName: "Browser Tests",
                    SurfaceId: "browser-1",
                    SurfaceName: "Fixture",
                    Kind: SurfaceKind.Browser,
                    Url: new Uri(fixturePath).AbsoluteUri,
                    Title: "ECodeX Browser Scripting P0"),
            ],
            _ => CreateFixtureSnapshot(),
            request =>
            {
                actionRequests.Add(request);
                return request.Action switch
                {
                    BrowserScriptingActionKind.Eval => BrowserScriptingActionOutcome.FromValue("saved"),
                    BrowserScriptingActionKind.Screenshot => BrowserScriptingActionOutcome.FromValue(new { contentType = "image/png" }),
                    _ => BrowserScriptingActionOutcome.FromValue(new { ok = true }),
                };
            });
        var surfaceRef = BrowserScriptingService.CreateSurfaceRef("browser-1");

        service.GetSnapshot(surfaceRef).Success.Should().BeTrue();
        service.Click(surfaceRef, BrowserScriptingLocator.TestId("save-button")).Success.Should().BeTrue();
        service.Fill(surfaceRef, BrowserScriptingLocator.TestId("email-input"), "codex@example.com").Success.Should().BeTrue();
        service.Eval(surfaceRef, "window.__ecodexFixture.clicked").Success.Should().BeTrue();
        service.Screenshot(surfaceRef).Success.Should().BeTrue();

        actionRequests.Select(request => request.Action).Should().Equal(
            BrowserScriptingActionKind.Click,
            BrowserScriptingActionKind.Fill,
            BrowserScriptingActionKind.Eval,
            BrowserScriptingActionKind.Screenshot);
        actionRequests[1].Value.Should().Be("codex@example.com");
        actionRequests.Should().OnlyContain(request => request.SurfaceId == "browser-1");
    }

    [Fact]
    [Trait("Category", Category)]
    public void P0Smoke_StaleSurfaceRefReturnsStableError()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            new(
                WorkspaceId: "workspace-1",
                WorkspaceName: "Browser Tests",
                SurfaceId: "browser-1",
                SurfaceName: "Fixture",
                Kind: SurfaceKind.Browser,
                Url: "file:///fixture.html",
                Title: "Fixture"),
        };
        var service = new BrowserScriptingService(() => surfaces, _ => CreateFixtureSnapshot());
        var surfaceRef = service.TrackSurface(surfaces[0]);

        surfaces.Clear();
        var result = service.GetSnapshot(surfaceRef);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(V2ErrorCodes.StaleRef);
    }

    private static BrowserScriptingSnapshot CreateFixtureSnapshot()
    {
        return new BrowserScriptingSnapshot(new BrowserScriptingNode
        {
            NodeId = "root",
            Role = "document",
            Name = "ECodeX Browser Scripting P0",
            Children =
            [
                new BrowserScriptingNode
                {
                    NodeId = "heading",
                    Role = "heading",
                    Name = "Browser scripting fixture",
                    Text = "Browser scripting fixture",
                },
                new BrowserScriptingNode
                {
                    NodeId = "email",
                    Role = "textbox",
                    Name = "Email",
                    TestId = "email-input",
                },
                new BrowserScriptingNode
                {
                    NodeId = "save",
                    Role = "button",
                    Name = "Save",
                    Text = "Save",
                    TestId = "save-button",
                },
            ],
        });
    }
}
