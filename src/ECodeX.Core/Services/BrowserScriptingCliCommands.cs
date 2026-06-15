namespace ECodeX.Core.Services;

public static class BrowserScriptingCliCommands
{
    public const string Open = "BROWSER.OPEN";
    public const string New = "BROWSER.NEW";
    public const string OpenSplit = "BROWSER.OPEN_SPLIT";
    public const string Snapshot = "BROWSER.SNAPSHOT";
    public const string Click = "BROWSER.CLICK";
    public const string Fill = "BROWSER.FILL";
    public const string Hover = "BROWSER.HOVER";
    public const string Press = "BROWSER.PRESS";
    public const string Eval = "BROWSER.EVAL";
    public const string Screenshot = "BROWSER.SCREENSHOT";

    public static bool TryResolve(string subcommand, out string pipeCommand)
    {
        pipeCommand = subcommand.ToLowerInvariant() switch
        {
            "open" => Open,
            "new" => New,
            "open-split" or "split" => OpenSplit,
            "snapshot" => Snapshot,
            "click" => Click,
            "fill" => Fill,
            "hover" => Hover,
            "press" => Press,
            "eval" => Eval,
            "screenshot" => Screenshot,
            _ => "",
        };

        return pipeCommand.Length > 0;
    }
}
