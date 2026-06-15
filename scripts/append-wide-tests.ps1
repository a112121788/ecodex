$RepoRoot = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
$path = Join-Path $RepoRoot 'tests/ECodeX.Tests/CoreTests.cs'
$src = [System.IO.File]::ReadAllText($path, [System.Text.UTF8Encoding]::new($false))

# 锚点：TerminalBufferTests 类的结尾，紧靠 OscHandlerTests 之前
$anchor = "`r`n}`r`n`r`npublic class OscHandlerTests"

# 构建新的测试代码块。我们通过拼接各部分来嵌入表示 NUL 字符的
# C# 字符字面量 `'`\`0`'`，以避免 PowerShell 吞掉转义序列。
$nl = "`r`n"
$sq = [string][char]39
$bs = [string][char]92
$zero = "0"
$nulLiteral = $sq + $bs + $zero + $sq   # 生成：'\0'（4 个字符）

$tests = "`r`n" + @"

    [Fact]
    public void WriteChar_Ascii_AdvancesOneColumn()
    {
        var buffer = new TerminalBuffer(20, 3);
        buffer.WriteChar('A');
        buffer.CursorCol.Should().Be(1);
        buffer.CellAt(0, 0).Width.Should().Be(1);
    }

    [Fact]
    public void WriteChar_Cjk_AdvancesTwoColumnsAndPlacesPlaceholder()
    {
        var buffer = new TerminalBuffer(20, 3);
        buffer.WriteChar('中');

        buffer.CursorCol.Should().Be(2);
        buffer.CellAt(0, 0).Character.Should().Be('中');
        buffer.CellAt(0, 0).Width.Should().Be(2);
        buffer.CellAt(0, 1).Character.Should().Be($nulLiteral);
        buffer.CellAt(0, 1).Width.Should().Be(0);
    }

    [Fact]
    public void WriteString_Cjk_AdvancesColumnPerGlyph()
    {
        var buffer = new TerminalBuffer(20, 3);
        buffer.WriteString("中文");

        buffer.CursorCol.Should().Be(4);
        buffer.CellAt(0, 0).Character.Should().Be('中');
        buffer.CellAt(0, 0).Width.Should().Be(2);
        buffer.CellAt(0, 1).Width.Should().Be(0);
        buffer.CellAt(0, 2).Character.Should().Be('文');
        buffer.CellAt(0, 2).Width.Should().Be(2);
        buffer.CellAt(0, 3).Width.Should().Be(0);
    }

    [Fact]
    public void WriteChar_Cjk_AtRightEdge_WrapsToNextLine()
    {
        var buffer = new TerminalBuffer(4, 3);
        buffer.WriteString("abc");
        buffer.CursorCol.Should().Be(3);

        buffer.WriteChar('中');
        buffer.CursorRow.Should().Be(1);
        buffer.CursorCol.Should().Be(2);
    }
}
"@ -replace "`r?`n", "`r`n"

if (-not $src.Contains($anchor)) {
    Write-Host "FAIL: anchor not found"
    exit 1
}

$src = $src.Replace($anchor, $tests + "`r`n`r`npublic class OscHandlerTests")

[System.IO.File]::WriteAllText($path, $src, [System.Text.UTF8Encoding]::new($false))
Write-Host "ok, size: $((Get-Item $path).Length)"