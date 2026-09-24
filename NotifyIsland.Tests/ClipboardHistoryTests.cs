using System;
using System.Collections.Generic;
using Xunit;

namespace NotifyIsland.Tests;

public class ClipboardHistoryTests
{
    private static DateTimeOffset T(int seconds) => new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    [Fact]
    public void Capacity_IsClampedToHardCap()
    {
        Assert.Equal(50, new ClipboardHistory(50).Capacity);
        Assert.Equal(ClipboardHistory.HardCap, new ClipboardHistory(10_000).Capacity);
        Assert.Equal(1, new ClipboardHistory(0).Capacity);
    }

    [Fact]
    public void Push_AppendsAndRespectsCapacity()
    {
        var h = new ClipboardHistory(3);
        for (var i = 0; i < 5; i++)
            h.Push(ClipboardEntry.FromText($"t{i}", T(i)));
        Assert.Equal(3, h.Count);
        var snap = h.Snapshot();
        Assert.Equal("t2", snap[0].Text);
        Assert.Equal("t3", snap[1].Text);
        Assert.Equal("t4", snap[2].Text);
    }

    [Fact]
    public void Push_DedupesAgainstLatestIdentical()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("hello", T(0)));
        h.Push(ClipboardEntry.FromText("hello", T(5)));
        Assert.Equal(1, h.Count);
    }

    [Fact]
    public void Push_IgnoresNoneKind()
    {
        var h = new ClipboardHistory();
        h.Push(new ClipboardEntry { Kind = ClipboardItemKind.None });
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void PopLatest_DropsLast()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("a", T(0)));
        h.Push(ClipboardEntry.FromText("b", T(1)));
        h.PopLatest();
        Assert.Equal("a", h.Latest!.Text);
        h.PopLatest();
        Assert.Null(h.Latest);
    }

    [Fact]
    public void Clear_WipesAll()
    {
        var h = new ClipboardHistory();
        h.Push(ClipboardEntry.FromText("x", T(0)));
        h.Push(ClipboardEntry.FromText("y", T(1)));
        h.Clear();
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void BuildPayload_Text_TruncatesTitleAndBody()
    {
        var entry = ClipboardEntry.FromText(new string('x', 500), T(0));
        var p = ClipboardHistory.BuildPayload(entry, T(0));
        Assert.Equal(ClipboardItemKind.Text, p.ClipboardItemKind);
        Assert.Equal(ClipboardHistory.TitlePreviewChars, p.Title.Length);
        Assert.Equal(ClipboardHistory.BodyPreviewChars, p.Body.Length);
        Assert.Equal("Текст", p.Subtitle);
        Assert.Equal(T(0), p.ClipboardCapturedAt);
    }

    [Fact]
    public void BuildPayload_File_ShowsFileName()
    {
        var entry = ClipboardEntry.FromFile(@"C:\Users\serjo\report.pdf", T(0));
        var p = ClipboardHistory.BuildPayload(entry, T(0));
        Assert.Equal(ClipboardItemKind.File, p.ClipboardItemKind);
        Assert.Equal("report.pdf", p.Title);
        Assert.Equal("Файл", p.Subtitle);
        Assert.Equal(@"C:\Users\serjo\report.pdf", p.Body);
        Assert.Equal(new[] { @"C:\Users\serjo\report.pdf" }, p.ClipboardPaths);
    }

    [Fact]
    public void BuildPayload_MultiFile_CountsFilesInRussianPlural()
    {
        // Cases: 1 файл, 2 файла, 5 файлов, 21 файл, 22 файла, 25 файлов
        Assert.Equal("1 файл", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(new List<string> { "a" }, T(0)), T(0)).Title);
        Assert.Equal("2 файла", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(new List<string> { "a", "b" }, T(0)), T(0)).Title);
        Assert.Equal("5 файлов", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(5), T(0)), T(0)).Title);
        Assert.Equal("11 файлов", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(11), T(0)), T(0)).Title);
        Assert.Equal("21 файл", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(21), T(0)), T(0)).Title);
        Assert.Equal("22 файла", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(22), T(0)), T(0)).Title);
        Assert.Equal("25 файлов", ClipboardHistory.BuildPayload(ClipboardEntry.FromFiles(EnumerableRange(25), T(0)), T(0)).Title);
    }

    private static List<string> EnumerableRange(int n)
    {
        var list = new List<string>(n);
        for (var i = 0; i < n; i++) list.Add($"f{i}");
        return list;
    }

    [Fact]
    public void BuildPayload_SingleFile_InferredAsFileNotMulti()
    {
        var p = ClipboardHistory.BuildPayload(
            new ClipboardEntry { Kind = ClipboardItemKind.None, Paths = new List<string> { @"d:\x.txt" } },
            T(0));
        // Sanitize promotes None + single path → File.
        var sanitized = OverlayMachine.Sanitize(p);
        Assert.Equal(ClipboardItemKind.File, sanitized.ClipboardItemKind);
    }

    [Fact]
    public void BuildPayload_DefaultCapturedAt_UsesNow()
    {
        var entry = ClipboardEntry.FromText("x", default);
        var now = T(99);
        var p = ClipboardHistory.BuildPayload(entry, now);
        Assert.Equal(now, p.ClipboardCapturedAt);
    }

    [Fact]
    public void ClipboardEntry_Equality_DistinguishesText()
    {
        var a = ClipboardEntry.FromText("hi", T(0));
        var b = ClipboardEntry.FromText("hi", T(0));
        var c = ClipboardEntry.FromText("bye", T(0));
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ClipboardEntry_Equality_DistinguishesFilePaths()
    {
        var a = ClipboardEntry.FromFile(@"C:\a.txt", T(0));
        var b = ClipboardEntry.FromFile(@"C:\b.txt", T(0));
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void ClipboardEntry_Equality_DistinguishesMultiFileOrder()
    {
        var a = ClipboardEntry.FromFiles(new List<string> { "x", "y" }, T(0));
        var b = ClipboardEntry.FromFiles(new List<string> { "y", "x" }, T(0));
        Assert.False(a.Equals(b));
    }
}
