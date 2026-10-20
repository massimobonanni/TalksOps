using System;
using System.IO;

namespace TalksOps.AI
{
    /// <summary>
    /// Helper to locate and read prompt files packaged with the TalksOps.AI project.
    /// The prompt file is included as content and copied to the build output under a PromptFiles folder.
    /// </summary>
    public static class PromptProvider
    {
        public static string GetExtractEventDataPromptPath()
        {
            // The prompt file is copied preserving the PromptFiles relative folder; use AppContext.BaseDirectory to locate it in runtime environments.
            return Path.Combine(AppContext.BaseDirectory, "PromptFiles", "ExtractEventDataPrompt.txt");
        }

        public static string LoadExtractEventDataPromptText()
        {
            var path = GetExtractEventDataPromptPath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("ExtractEventDataPrompt.txt not found in output. Ensure the file is included in the TalksOps.AI project and CopyToOutputDirectory is set.", path);
            }

            return File.ReadAllText(path);
        }
    }
}
