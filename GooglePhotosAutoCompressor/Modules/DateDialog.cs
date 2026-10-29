using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    /// <summary>
    /// Google Photos' "Edit date and time" dialog, opened from the info panel of the photo viewer.
    /// Fields: Year, Month, Day, Hour, Minutes (24-hour in en-GB) and a time zone drop-down.
    /// </summary>
    internal static class DateDialog
    {
        /// <summary>Shows the viewer's info panel (with file name and date taken) if it is hidden.</summary>
        public static async Task ShowInfoPanel(IPage page)
        {
            ILocator panels = page.Locator(Selectors.DATE_TAKEN_PANEL);
            if (await DateSetter.WaitForAnyVisibleAsync(panels, 3000) == false)
            {
                await page.Locator(Selectors.OPEN_INFO_BUTTON).First.ClickAsync();
                await DateSetter.WaitForAnyVisibleAsync(panels, 5000);
            }
        }

        /// <summary>Opens the dialog for the item shown in the viewer (the info panel must be visible).</summary>
        public static async Task<ILocator> Open(IPage page)
        {
            ILocator datePanel = await DateSetter.FindActivePanel(page.Locator(Selectors.DATE_TAKEN_PANEL));
            await datePanel.ClickAsync();

            ILocator dialog = Dialog(page);
            await dialog.Locator(Selectors.DATE_YEAR_INPUT).WaitForAsync();
            return dialog;
        }

        /// <summary>Date, time and time zone shown in an open dialog. Time is null when a field cannot be read.</summary>
        public static async Task<(DateTime? takenAt, int? offsetMs)> Read(ILocator dialog)
        {
            DateTime? takenAt = TakenTime.FromFields(
                await Value(dialog, Selectors.DATE_YEAR_INPUT),
                await Value(dialog, Selectors.DATE_MONTH_INPUT),
                await Value(dialog, Selectors.DATE_DAY_INPUT),
                await Value(dialog, Selectors.DATE_HOUR_INPUT),
                await Value(dialog, Selectors.DATE_MINUTES_INPUT));

            int? offsetMs = TakenTime.ParseOffsetMs(await dialog.Locator(Selectors.TIME_ZONE_BOX).First.InnerTextAsync());
            return (takenAt, offsetMs);
        }

        /// <summary>Escape closes the dialog without saving (a second time if the time zone list was open).</summary>
        public static async Task CloseWithoutSaving(IPage page, ILocator dialog)
        {
            ILocator year = dialog.Locator(Selectors.DATE_YEAR_INPUT);
            for (int i = 0; i < 3 && await year.IsVisibleAsync(); i++)
            {
                await page.Keyboard.PressAsync("Escape");
                await Task.Delay(500);
            }

            await year.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        }

        /// <summary>
        /// Types the date (and the time and time zone when known) and saves. Without a known time only the
        /// date is typed, which keeps the time Google read from the file. If the fields do not end up showing
        /// exactly that, nothing is saved and the next run tries again.
        /// </summary>
        public static async Task Save(IPage page, ILocator dialog, DateOnly date, DateTime? takenAt, int? offsetMs)
        {
            try
            {
                await Fill(dialog, date, takenAt, offsetMs);
            }
            catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException)
            {
                await CloseWithoutSaving(page, dialog);
                throw;
            }

            await dialog.Locator(Selectors.DIALOG_DEFAULT_BUTTON).First.ClickAsync();
            await dialog.Locator(Selectors.DATE_YEAR_INPUT).WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }

        /// <summary>Types the values into the open dialog without saving, then reads them back to check them.</summary>
        public static async Task Fill(ILocator dialog, DateOnly date, DateTime? takenAt, int? offsetMs)
        {
            // Time zone first: picking one moves the shown time to keep the same moment.
            if (offsetMs is int wanted)
            {
                await SelectTimeZone(dialog, wanted);
            }

            DateOnly day = takenAt is DateTime t ? DateOnly.FromDateTime(t) : date;
            await dialog.Locator(Selectors.DATE_YEAR_INPUT).First.FillAsync(day.Year.ToString());
            await dialog.Locator(Selectors.DATE_MONTH_INPUT).First.FillAsync(day.Month.ToString());
            await dialog.Locator(Selectors.DATE_DAY_INPUT).First.FillAsync(day.Day.ToString());

            if (takenAt is DateTime time)
            {
                await dialog.Locator(Selectors.DATE_HOUR_INPUT).First.FillAsync(time.Hour.ToString("00"));
                await dialog.Locator(Selectors.DATE_MINUTES_INPUT).First.FillAsync(time.Minute.ToString("00"));
            }

            (DateTime? shown, int? shownOffset) = await Read(dialog);
            bool ok = shown != null && DateOnly.FromDateTime(shown.Value) == day
                      && (takenAt == null || shown == takenAt)
                      && (offsetMs == null || shownOffset == offsetMs);
            if (ok == false)
            {
                throw new InvalidOperationException(
                    $"date dialog shows {(shown is DateTime s ? TakenTime.Describe(s, shownOffset) : "nothing readable")}, " +
                    $"expected {(takenAt is DateTime w ? TakenTime.Describe(w, offsetMs) : day.ToString("d MMM yyyy"))}");
            }
        }

        private static async Task SelectTimeZone(ILocator dialog, int offsetMs)
        {
            ILocator box = dialog.Locator(Selectors.TIME_ZONE_BOX).First;
            if (TakenTime.ParseOffsetMs(await box.InnerTextAsync()) == offsetMs)
            {
                return;
            }

            await box.ClickAsync();
            ILocator option = dialog.Locator(Selectors.TimeZoneOption(offsetMs)).First;
            await option.WaitForAsync(new() { Timeout = 5000 });
            await option.ClickAsync();
            await Task.Delay(300);

            int? now = TakenTime.ParseOffsetMs(await box.InnerTextAsync());
            if (now != offsetMs)
            {
                throw new InvalidOperationException($"time zone {TakenTime.FormatOffset(offsetMs)} could not be selected");
            }
        }

        private static ILocator Dialog(IPage page) =>
            page.Locator(Selectors.DIALOG).Filter(new() { Has = page.Locator(Selectors.DATE_YEAR_INPUT) }).First;

        private static async Task<string?> Value(ILocator dialog, string selector) =>
            await dialog.Locator(selector).First.InputValueAsync(new() { Timeout = 5000 });
    }
}
