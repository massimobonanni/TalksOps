using System;
using System.IO;
using Xunit;

namespace TalksOps.AI.Tests
{
    public class PromptFilesTests
    {
        [Fact]
        public void ExtractEventDataPrompt_Should_Exist_InOutput()
        {
            // The build output for the test run should include the prompt file copied by the TalksOps.AI project.
            var candidate = Path.Combine(AppContext.BaseDirectory, "PromptFiles", "ExtractEventDataPrompt.txt");
            Assert.True(File.Exists(candidate), $"Expected prompt file at '{candidate}' but it was not found.");
        }
    }
}
