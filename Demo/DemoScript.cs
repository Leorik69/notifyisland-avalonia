namespace NotifyIsland.Demo;

internal static class DemoScript
{
    private static int _index;

    public static void Reset() => _index = 0;

    public static void Next(OverlayMachine machine)
    {
        var steps = new (OverlayCommand cmd, OverlayPayload data)[]
        {
            (OverlayCommand.Clear, new OverlayPayload()),
            (OverlayCommand.Expand, new OverlayPayload { Title = "Сегодня", Body = "Ясно, 18°" }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "Telegram", Body = "Привет", Template = NotifyTemplates.Chat, DurationMs = 2500 }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "Почта", Body = "Счёт за сентябрь", Template = NotifyTemplates.Mail, DurationMs = 2500 }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "14:00", Body = "Созвон", Template = NotifyTemplates.Calendar, DurationMs = 2500 }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "Входящий", Body = "Алексей", Template = NotifyTemplates.Call, DurationMs = 6000 }),
            (OverlayCommand.SetProgress, new OverlayPayload { Title = "Файл.zip", Progress = 0.12, Template = NotifyTemplates.Download }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "Готово", Body = "Скачано", Template = NotifyTemplates.Complete, DurationMs = 2200 }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "Батарея", Body = "15%", Template = NotifyTemplates.Warn, DurationMs = 4000 }),
            (OverlayCommand.SetTimer, new OverlayPayload { Title = "Фокус", RemainingSeconds = 45, Template = NotifyTemplates.Focus }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "Сеть", Body = "Wi‑Fi подключён", Template = NotifyTemplates.System, DurationMs = 2500 }),
            (OverlayCommand.Stack, new OverlayPayload { Title = "Пачка", Subtitle = "Почта · 2", Line2 = "Календарь · 10 мин", Template = NotifyTemplates.Queue }),
            (OverlayCommand.SetMedia, new OverlayPayload { Title = "Night Drive", Subtitle = "Local Radio", Progress = 0.33, Playing = true }),
            (OverlayCommand.SetError, new OverlayPayload { Title = "Сеть", Body = "Нет ответа сервера" }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "こんにちは", Body = "長い通知テキストのテスト", DurationMs = 2500 }),
            (OverlayCommand.Notify, new OverlayPayload { Title = "مرحبا", Body = "RTL sample", DurationMs = 2500 }),
            (OverlayCommand.SetProgress, new OverlayPayload { Title = "big-file.iso", Progress = 0.4, EtaSeconds = 90, Cancellable = true, Template = NotifyTemplates.Download }),
            (OverlayCommand.SetTimer, new OverlayPayload { Title = "00:00", RemainingSeconds = 0.2, Template = NotifyTemplates.Focus }),
        };
        var step = steps[_index % steps.Length];
        _index++;
        machine.Dispatch(step.cmd, step.data);
    }
}
