using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class DateSetter
    {
        internal static async Task SetCompressedFilesDates(IPage page, FileItemsData files)
        {
            Console.WriteLine("\n===\nSetCompressedFilesDates\n===\n");

            await page.Locator("[data-latest-bg]").First.ClickAsync();

            ILocator fileNamePanels = page.Locator("[aria-label^='Filename:']");

            await WaitForAnyVisibleAsync(fileNamePanels, 3000);

            if (await fileNamePanels.CountAsync() == 0)
            {
                await page.Locator("button[aria-label='Open info']").ClickAsync();
            }

            await WaitForAnyVisibleAsync(fileNamePanels, 3000);

            ILocator nextPhotoButton = page.Locator("[aria-label='View next photo']");

            int notFoundCount = 0;
            for (int i = 0; i < 10_000 && notFoundCount < 10; i++)
            {
                ILocator fileNamePanel = await FindActivePanel(fileNamePanels);
                string fileNameText = await fileNamePanel.InnerTextAsync();
                FileItem file = files.Items.FirstOrDefault(x => Path.GetFileName(x.CompressPath) == fileNameText);

                if (file == null || file.DateSet)
                {
                    notFoundCount++;
                    await nextPhotoButton.First.ClickAsync();
                    await Task.Delay(1000);
                    continue;
                }

                ILocator datePanels = page.Locator("[aria-label^='Date taken:']");
                ILocator datePanel = await FindActivePanel(datePanels);
                await datePanel.ClickAsync();

                ILocator dateDialog = page.Locator("[role='dialog']");
                await dateDialog.WaitForAsync();

                await dateDialog.Locator("input[aria-label='Year']").First.FillAsync(file.Date.Year.ToString());
                await dateDialog.Locator("input[aria-label='Month']").First.FillAsync(file.Date.Month.ToString());
                await dateDialog.Locator("input[aria-label='Day']").First.FillAsync(file.Date.Day.ToString());
                await dateDialog.Locator("button[data-mdc-dialog-button-default]").First.ClickAsync();

                Console.WriteLine($"date set for: {Path.GetFileName(file.CompressPath)} {file.Date}");

                file.DateSet = true;
                files.Save();

                await nextPhotoButton.First.ClickAsync();
                await Task.Delay(1000);
            }
        }

        private static async Task<ILocator> FindActivePanel(ILocator panels)
        {
            int count = await panels.CountAsync();
            for (int i = 0; i < count; i++)
            {
                ILocator panel = panels.Nth(i);
                LocatorBoundingBoxResult? box = await panel.BoundingBoxAsync();

                if (box == null || box.Width <= 0)
                {
                    continue;
                }

                return panel;
            }

            throw new Exception("panel not found");
        }

        public static async Task<bool> WaitForAnyVisibleAsync(ILocator locator, int timeout)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);

            while (DateTime.UtcNow < deadline)
            {
                var count = await locator.CountAsync();
                for (var i = 0; i < count; i++)
                {
                    if (await locator.Nth(i).IsVisibleAsync())
                    {
                        return true;
                    }
                }

                await Task.Delay(100);
            }

            return false;
        }
    }
}