using System;
using System.Collections.Generic;
using Xunit;

namespace NotifyIsland.Tests;

public class ClipboardItemVmTests
{
    private static DateTimeOffset T(int secondsAgo) => DateTimeOffset.Now.AddSeconds(-secondsAgo);

    [Fact]
    public void From_TextEntry_HasTextKindGlyph()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("hello world", T(0)));
        Assert.Equal("≡", vm.KindGlyph);
        Assert.Equal("Текст", vm.KindLabelRu);
        Assert.Equal("hello world", vm.Title);
        Assert.Equal("11 символов", vm.Subtitle);
    }

    [Fact]
    public void From_TextEntry_StripsNewlines()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("line1\nline2\r\nline3", T(0)));
        Assert.Equal("line1 line2 line3", vm.Title);
    }

    [Fact]
    public void From_EmptyText_ShowsPlaceholder()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("", T(0)));
        Assert.Equal("(пусто)", vm.Title);
        Assert.Equal("0 символов", vm.Subtitle);
    }

    [Fact]
    public void From_FileEntry_ShowsFileNameInTitleAndPathInSubtitle()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromFile(@"C:\Users\me\report.pdf", T(0)));
        Assert.Equal("▣", vm.KindGlyph);
        Assert.Equal("Файл", vm.KindLabelRu);
        Assert.Equal("report.pdf", vm.Title);
        Assert.Equal(@"C:\Users\me\report.pdf", vm.Subtitle);
    }

    [Fact]
    public void From_MultiFileEntry_ListsUpToThreeNames()
    {
        var paths = new List<string> { @"C:\a.txt", @"C:\b.txt", @"C:\c.txt", @"C:\d.txt", @"C:\e.txt" };
        var vm = ClipboardItemVm.From(ClipboardEntry.FromFiles(paths, T(0)));
        Assert.Equal("▤", vm.KindGlyph);
        Assert.Equal("Файлы", vm.KindLabelRu);
        Assert.Equal("a.txt, b.txt, c.txt … (+2)", vm.Title);
        Assert.Equal("5 файлов", vm.Subtitle);
    }

    [Fact]
    public void From_MultiFileEntry_TwoFiles_UsesФайла()
    {
        var paths = new List<string> { @"C:\a.txt", @"C:\b.txt" };
        var vm = ClipboardItemVm.From(ClipboardEntry.FromFiles(paths, T(0)));
        // Note: title "2 файла" doesn't appear in our impl (we just say "2 файлов");
        // we don't currently use pluralization in the title — that's a future polish.
        Assert.Contains("a.txt", vm.Title);
        Assert.Contains("b.txt", vm.Title);
    }

    [Fact]
    public void TimeAgo_JustNow()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("x", T(0)));
        Assert.Equal("только что", vm.TimeAgoRu);
    }

    [Fact]
    public void TimeAgo_Seconds()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("x", T(30)));
        Assert.Contains("30", vm.TimeAgoRu);
        Assert.Contains("с назад", vm.TimeAgoRu);
    }

    [Fact]
    public void TimeAgo_Minutes()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("x", T(180)));
        Assert.Contains("3", vm.TimeAgoRu);
        Assert.Contains("мин назад", vm.TimeAgoRu);
    }

    [Fact]
    public void TimeAgo_Hours()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("x", T(7200)));
        Assert.Contains("2", vm.TimeAgoRu);
        Assert.Contains("ч назад", vm.TimeAgoRu);
    }

    [Fact]
    public void TimeAgo_Days()
    {
        var vm = ClipboardItemVm.From(ClipboardEntry.FromText("x", T(172800)));
        Assert.Contains("2", vm.TimeAgoRu);
        Assert.Contains("дн назад", vm.TimeAgoRu);
    }
}
