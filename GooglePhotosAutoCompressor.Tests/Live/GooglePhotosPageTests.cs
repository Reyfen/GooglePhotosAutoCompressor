using GooglePhotosAutoCompressor.Modules;
using Microsoft.Playwright;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace GooglePhotosAutoCompressor.Tests.Live
{
    /// <summary>
    /// Live checks of the Google Photos pages and clicks each step of the app relies on, so a change on
    /// Google's side shows up as a failing test instead of a broken run. Uses the real selectors from
    /// <see cref="Selectors"/>, then asks the local Claude Code to look at a screenshot and confirm the page
    /// is in the expected state. When a selector or click fails, Claude Code describes what the page shows instead.
    ///
    /// Nothing is changed in the account: no upload, no delete, no date saved. The download test starts a
    /// download and cancels it. The bin dialog test only runs with GPAC_TEST_LIVE_BIN_DIALOG=1.
    ///
    /// Needs: Claude Code, Google Chrome and a signed-in profile (run the app once). Leave out with
    /// dotnet test --filter "Category!=Live"
    /// </summary>
    [Collection(LivePhotosCollection.NAME)]
    [Trait("Category", "Live")]
    public sealed class GooglePhotosPageTests
    {
        private readonly LivePhotosFixture _f;
        private readonly ITestOutputHelper _out;
        private IPage Page => _f.Page;

        public GooglePhotosPageTests(LivePhotosFixture fixture, ITestOutputHelper output)
        {
            _f = fixture;
            _out = output;
        }

        private void RequireLive() => Skip.If(_f.SkipReason != null, _f.SkipReason);

        // ---------- Home / sign-in ----------

        [SkippableFact]
        public async Task Home_page_is_signed_in_and_in_English_UK()
        {
            RequireLive();
            await Page.GotoAsync(LivePhotosFixture.HOME_URL);

            await Expect("the photo library to load (a first item with [data-latest-bg])", () =>
                Page.Locator(Selectors.LIBRARY_FIRST_ITEM).First.WaitForAsync());

            string lang = await Page.EvaluateAsync<string>("document.documentElement.lang");
            Assert.True(lang.StartsWith("en", StringComparison.OrdinalIgnoreCase),
                $"Page language is '{lang}', expected {Const.LOCALE}. Every selector relies on English labels.");

            await ExpectScreen("home",
                "Is this the signed-in Google Photos main library (a grid of photos or videos), with the interface in English?");
        }

        // ---------- Large files list (scan, download, delete steps) ----------

        [SkippableFact]
        public async Task Large_files_scan_reads_video_tiles()
        {
            RequireLive();
            List<FileItem> items = new();
            using CancellationTokenSource cts = new();

            bool sawWholeList = await LargeFileLocator.EnumearteFiles(Page, (_, item) =>
            {
                items.Add(item);
                if (items.Count >= 30) cts.Cancel();
                return Task.CompletedTask;
            }, cts.Token);

            foreach (FileItem i in items.Take(5))
                _out.WriteLine($"read tile: {i.MediaKey[..Math.Min(12, i.MediaKey.Length)]}… {i.Size / 1024 / 1024} MB, {i.Duration}, {i.Date}");
            _out.WriteLine($"read {items.Count} tiles, whole list seen: {sawWholeList}");

            if (items.Count == 0)
                await Fail("the large files page to list video tiles whose size, date and duration can be read " +
                           $"(tiles: {Selectors.LARGE_FILE_TILE})", "no tile could be read");

            Assert.All(items, i => Assert.True(i.Size > 0 && i.Duration > TimeSpan.Zero));
            Assert.Equal(items.Count, items.Select(i => i.MediaKey).Distinct().Count());

            await ExpectScreen("large-files",
                "Is this the Google Photos storage page listing large photos and videos (as a list or a grid), " +
                "with a file size shown on the items?");
        }

        [SkippableFact]
        public async Task Clicking_a_large_file_selects_it_and_shows_download_and_bin_buttons()
        {
            RequireLive();
            FileItem item = await SelectFirstLargeFile();

            await Expect($"a visible Download button ({Selectors.DOWNLOAD_BUTTON}) after selecting one item", () => FirstVisible(Selectors.DOWNLOAD_BUTTON));
            await Expect($"a visible Move to bin button ({Selectors.MOVE_TO_BIN_BUTTON}) after selecting one item", () => FirstVisible(Selectors.MOVE_TO_BIN_BUTTON));

            await ExpectScreen("large-files-selected",
                "Is exactly one item selected (marked with a tick), with a bar at the top showing '1 selected' " +
                "(or similar) and icons for download and moving to the bin?");

            await Page.Locator(Selectors.TileByMediaKey(item.MediaKey)).First.ClickAsync(); // unselect
        }

        [SkippableFact]
        public async Task Download_button_starts_a_single_file_download()
        {
            RequireLive();
            await SelectFirstLargeFile();

            IDownload download = null!;
            await Expect("clicking Download to start a file download within 60 s", async () =>
            {
                ILocator button = await FirstVisible(Selectors.DOWNLOAD_BUTTON);
                download = await Page.RunAndWaitForDownloadAsync(() => button.ClickAsync(), new() { Timeout = 60_000 });
            });

            string name = download.SuggestedFilename;
            await download.CancelAsync();
            _out.WriteLine($"download started: {name} (cancelled)");

            Assert.False(name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                $"Got a zip ({name}): more than one item was selected by one click.");
            Assert.Matches(@"\.(mp4|mov|m4v|3gp|avi|mkv|webm|jpe?g|png|heic|gif|webp)$", name.ToLowerInvariant());
        }

        /// <summary>
        /// Opens the "Move to bin" confirmation and closes it with Escape. Off by default: if Google ever drops the
        /// confirmation, the click alone would move the item to the bin (it can be restored from the bin for 60 days).
        /// </summary>
        [SkippableFact]
        public async Task Move_to_bin_asks_for_confirmation()
        {
            RequireLive();
            Skip.If(Environment.GetEnvironmentVariable("GPAC_TEST_LIVE_BIN_DIALOG") != "1",
                "Bin dialog test is off by default. Set GPAC_TEST_LIVE_BIN_DIALOG=1 to run it (use a test account; worst case one item goes to the bin).");

            FileItem item = await SelectFirstLargeFile();
            await (await FirstVisible(Selectors.MOVE_TO_BIN_BUTTON)).ClickAsync();

            await Expect($"a confirmation dialog with a default button ({Selectors.DIALOG_DEFAULT_BUTTON})", () =>
                Page.Locator(Selectors.DIALOG_DEFAULT_BUTTON).First.WaitForAsync());
            await ExpectScreen("bin-dialog", "Is a dialog open asking to confirm moving an item to the bin, with a cancel option?");

            await Page.Keyboard.PressAsync("Escape");
            await Page.Locator(Selectors.DIALOG_DEFAULT_BUTTON).First.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
            Assert.True(await Page.Locator(Selectors.TileByMediaKey(item.MediaKey)).CountAsync() > 0, "The item disappeared after cancelling.");
        }

        // ---------- Upload ----------

        [SkippableFact]
        public async Task Create_menu_opens_a_file_picker_for_upload()
        {
            RequireLive();
            await Page.GotoAsync(LivePhotosFixture.HOME_URL);

            await Expect($"the Create button ({Selectors.CREATE_BUTTON}) to open its menu", () =>
                Page.Locator(Selectors.CREATE_BUTTON).ClickAsync());
            await ExpectScreen("create-menu", "Is a menu open that offers to import or upload photos from the computer?");

            IFileChooser chooser = null!;
            await Expect($"'Import photos from your computer' ({Selectors.IMPORT_FROM_COMPUTER}) to open a file picker", async () =>
                chooser = await Page.RunAndWaitForFileChooserAsync(() => Page.Locator(Selectors.IMPORT_FROM_COMPUTER).ClickAsync()));

            Assert.True(chooser.IsMultiple || chooser.Element != null);
            // No file is given to the picker, so nothing is uploaded.
            await Page.Keyboard.PressAsync("Escape");
        }

        // ---------- Viewer and date (date step) ----------

        [SkippableFact]
        public async Task Viewer_shows_file_name_and_date_and_moves_to_next_item()
        {
            RequireLive();
            (string fileName, string date) = await OpenFirstItemInViewer();
            string firstUrl = Page.Url;
            _out.WriteLine($"first item: {fileName}, {date}, key {DateSetter.ParseMediaKeyFromUrl(firstUrl)}");

            Assert.NotNull(DateSetter.ParseMediaKeyFromUrl(firstUrl));
            await ExpectScreen("viewer",
                "Is a single photo or video open in a viewer, with an info panel showing details such as the file name and date?");

            await Expect($"'View next photo' ({Selectors.NEXT_PHOTO_BUTTON}) to open the next item", async () =>
            {
                await Page.Locator(Selectors.NEXT_PHOTO_BUTTON).First.ClickAsync();
                await Page.WaitForURLAsync(url => url != firstUrl, new() { Timeout = 10_000 });
            });
            Assert.NotEqual(DateSetter.ParseMediaKeyFromUrl(firstUrl), DateSetter.ParseMediaKeyFromUrl(Page.Url));
        }

        [SkippableFact]
        public async Task Date_dialog_has_year_month_day_fields_and_closes_without_saving()
        {
            RequireLive();
            (_, string dateBefore) = await OpenFirstItemInViewer();

            ILocator dialog = Page.Locator(Selectors.DIALOG);
            await Expect($"clicking 'Date taken' ({Selectors.DATE_TAKEN_PANEL}) to open the edit date dialog", async () =>
            {
                await (await FirstVisible(Selectors.DATE_TAKEN_PANEL)).ClickAsync();
                await dialog.First.WaitForAsync();
            });

            foreach (string input in new[] { Selectors.DATE_YEAR_INPUT, Selectors.DATE_MONTH_INPUT, Selectors.DATE_DAY_INPUT,
                                             Selectors.DATE_HOUR_INPUT, Selectors.DATE_MINUTES_INPUT })
            {
                await Expect($"the date dialog to have the field {input}", () => dialog.Locator(input).First.WaitForAsync());
                string value = await dialog.Locator(input).First.InputValueAsync();
                Assert.False(string.IsNullOrWhiteSpace(value), $"{input} is empty");
            }
            await Expect($"the date dialog to have a save button ({Selectors.DIALOG_DEFAULT_BUTTON})", () =>
                dialog.Locator(Selectors.DIALOG_DEFAULT_BUTTON).First.WaitForAsync());

            await Expect($"the date dialog to show a time zone ({Selectors.TIME_ZONE_BOX}) like 'GMT+01:00'", async () =>
            {
                string zone = await dialog.Locator(Selectors.TIME_ZONE_BOX).First.InnerTextAsync();
                if (TakenTime.ParseOffsetMs(zone) == null) throw new InvalidOperationException($"time zone text is '{zone}'");
            });

            await ExpectScreen("date-dialog", "Is a dialog open for editing the date and time, with separate fields for year, month, day, hour and minutes, and a time zone?");

            // Close without saving, and check nothing changed.
            await Page.Keyboard.PressAsync("Escape");
            await Expect("Escape to close the date dialog", () =>
                dialog.Locator(Selectors.DATE_YEAR_INPUT).First.WaitForAsync(new() { State = WaitForSelectorState.Hidden }));
            string dateAfter = (await (await FirstVisible(Selectors.DATE_TAKEN_PANEL)).GetAttributeAsync("aria-label")) ?? "";
            Assert.Equal(dateBefore, dateAfter);
        }

        [SkippableFact]
        public async Task Original_date_time_and_zone_are_read_without_changing_them()
        {
            RequireLive();
            // Same path as the "read date and time" step: a large item from the list, opened by its media key.
            FileItem? item = null;
            using CancellationTokenSource cts = new();
            await LargeFileLocator.EnumearteFiles(Page, (_, i) =>
            {
                item ??= i;
                cts.Cancel();
                return Task.CompletedTask;
            }, cts.Token);
            if (item == null)
                await Fail("a tile on the large files page", "the scanner found no readable tile");

            (DateTime? takenAt, int? offsetMs) = (null, null);
            await Expect($"the item to open by its media key ({Selectors.ViewerUrl("<key>")}) and its date dialog to show date, time and time zone", async () =>
                (takenAt, offsetMs) = await TakenTimeReader.ReadOne(Page, item!.MediaKey));

            _out.WriteLine($"taken: {(takenAt is DateTime t ? TakenTime.Describe(t, offsetMs) : "?")}, list date {item!.Date}");
            Assert.NotNull(takenAt);
            Assert.NotNull(offsetMs);
            Assert.Equal(item.Date, DateOnly.FromDateTime(takenAt!.Value));
            Assert.False(await Page.Locator(Selectors.DATE_YEAR_INPUT).First.IsVisibleAsync(), "the date dialog is still open");

            // Reading again gives the same value: nothing was saved.
            (DateTime? again, int? againOffset) = await TakenTimeReader.ReadOne(Page, item.MediaKey);
            Assert.Equal(takenAt, again);
            Assert.Equal(offsetMs, againOffset);
            await ExpectScreen("original-viewer", "Is a single photo or video open in a viewer with an info panel, and no dialog open?");
        }

        [SkippableFact]
        public async Task Date_dialog_takes_a_new_time_and_zone_and_cancel_keeps_the_old_ones()
        {
            RequireLive();
            // What the date step types for a copy, but closed with Escape instead of Save.
            (string fileName, _) = await OpenFirstItemInViewer();
            ILocator dialog = await DateDialog.Open(Page);
            (DateTime? before, int? beforeOffset) = await DateDialog.Read(dialog);
            Assert.NotNull(before);
            Assert.NotNull(beforeOffset);

            DateTime otherTime = before!.Value.Date.AddHours(before.Value.Hour == 3 ? 4 : 3).AddMinutes(7);
            int otherOffset = beforeOffset == 19_800_000 ? 32_400_000 : 19_800_000; // GMT+05:30 or GMT+09:00
            try
            {
                await Expect($"typing {TakenTime.Describe(otherTime, otherOffset)} into the date dialog (hour, minutes and time zone list)", () =>
                    DateDialog.Fill(dialog, DateOnly.FromDateTime(otherTime), otherTime, otherOffset));
                await ExpectScreen("date-dialog-filled",
                    $"Does the 'Edit date and time' dialog show the time {otherTime:HH:mm} and the time zone {TakenTime.FormatOffset(otherOffset)}?");
            }
            finally
            {
                await DateDialog.CloseWithoutSaving(Page, dialog);
            }

            dialog = await DateDialog.Open(Page);
            (DateTime? after, int? afterOffset) = await DateDialog.Read(dialog);
            await DateDialog.CloseWithoutSaving(Page, dialog);
            _out.WriteLine($"{fileName}: {TakenTime.Describe(before.Value, beforeOffset)} -> typed {TakenTime.Describe(otherTime, otherOffset)} -> after cancel {(after is DateTime a ? TakenTime.Describe(a, afterOffset) : "?")}");
            Assert.Equal(before, after);
            Assert.Equal(beforeOffset, afterOffset);
        }

        // ---------- helpers ----------

        /// <summary>Same path as the download and delete steps: the scanner finds a video tile and it is clicked.</summary>
        private async Task<FileItem> SelectFirstLargeFile()
        {
            FileItem? selected = null;
            using CancellationTokenSource cts = new();
            await LargeFileLocator.EnumearteFiles(Page, async (tile, item) =>
            {
                if (selected != null) return;
                selected = item;
                await tile.ClickAsync();
                await Task.Delay(300);
                cts.Cancel();
            }, cts.Token);

            if (selected == null)
                await Fail("a video tile on the large files page to select", "the scanner found no readable tile");
            return selected!;
        }

        /// <summary>Same path as the date step: open the newest item and make sure the info panel is shown.</summary>
        private async Task<(string fileName, string dateLabel)> OpenFirstItemInViewer()
        {
            // The date step starts from "Recently added" (newest uploads first), not the library.
            await Page.GotoAsync(Selectors.RECENTLY_ADDED_URL);
            await Expect($"clicking the first item of Recently added ({Selectors.RECENTLY_ADDED_URL}, {Selectors.LIBRARY_FIRST_ITEM}) to open the viewer", async () =>
            {
                await Page.Locator(Selectors.LIBRARY_FIRST_ITEM).First.ClickAsync();
                await Page.WaitForURLAsync("**/photo/**", new() { Timeout = 10_000 });
            });

            ILocator fileNames = Page.Locator(Selectors.FILE_NAME_PANEL);
            if (!await DateSetter.WaitForAnyVisibleAsync(fileNames, 3000))
            {
                await Expect($"the 'Open info' button ({Selectors.OPEN_INFO_BUTTON}) to show the info panel", async () =>
                {
                    await Page.Locator(Selectors.OPEN_INFO_BUTTON).First.ClickAsync();
                    if (!await DateSetter.WaitForAnyVisibleAsync(fileNames, 5000))
                        throw new TimeoutException($"{Selectors.FILE_NAME_PANEL} not visible");
                });
            }

            string fileName = "";
            string date = "";
            await Expect($"the info panel to show the file name ({Selectors.FILE_NAME_PANEL}) and date taken ({Selectors.DATE_TAKEN_PANEL})", async () =>
            {
                fileName = (await (await FirstVisible(Selectors.FILE_NAME_PANEL)).InnerTextAsync()).Trim();
                date = (await (await FirstVisible(Selectors.DATE_TAKEN_PANEL)).GetAttributeAsync("aria-label")) ?? "";
                if (fileName.Length == 0) throw new InvalidOperationException("file name panel is empty");
            });
            return (fileName, date);
        }

        /// <summary>The steps pick the first element with a real size, since Google keeps hidden copies in the page.</summary>
        private async Task<ILocator> FirstVisible(string selector, int timeoutMs = 10_000)
        {
            ILocator all = Page.Locator(selector);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                int count = await all.CountAsync();
                for (int i = 0; i < count; i++)
                {
                    LocatorBoundingBoxResult? box = await all.Nth(i).BoundingBoxAsync();
                    if (box is { Width: > 0, Height: > 0 }) return all.Nth(i);
                }
                await Task.Delay(200);
            } while (DateTime.UtcNow < deadline);

            throw new TimeoutException($"no visible element matches {selector}");
        }

        /// <summary>Runs a page action; if it fails, Claude Code explains what the page shows instead.</summary>
        private async Task Expect(string expected, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException)
            {
                await Fail(expected, e.Message.Split('\n')[0]);
            }
        }

        private async Task Fail(string expected, string error)
        {
            string shot = await Screenshot("FAILED-" + Guid.NewGuid().ToString("N")[..6]);
            string explanation;
            try { explanation = ClaudeCode.DescribeProblem(shot, expected); }
            catch (Exception e) { explanation = $"(Claude Code could not look at the screenshot: {e.Message})"; }

            throw new XunitException(
                $"Google Photos page check failed. Expected: {expected}\n" +
                $"Error: {error}\nURL: {Page.Url}\nScreenshot: {shot}\nClaude Code says: {explanation}");
        }

        /// <summary>Asks Claude Code to confirm the page looks as expected.</summary>
        private async Task ExpectScreen(string name, string question)
        {
            string shot = await Screenshot(name);
            ScreenVerdict v = ClaudeCode.CheckScreenshot(shot, question);
            _out.WriteLine($"Claude Code ({ClaudeCode.Model}) on {name}: ok={v.Ok}, {v.Reason}");
            Assert.True(v.Ok, $"Claude Code says the page is not as expected.\nQuestion: {question}\nAnswer: {v.Reason}\nURL: {Page.Url}\nScreenshot: {shot}");
        }

        private async Task<string> Screenshot(string name)
        {
            await Task.Delay(500); // let animations settle
            string path = Path.Combine(_f.ScreenshotDir, name + ".png");
            await Page.ScreenshotAsync(new() { Path = path });
            return path;
        }
    }
}
