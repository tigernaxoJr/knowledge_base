using System.Reflection;
using Assistant.Core.LlmClient;
using Xunit;

namespace Assistant.Core.Tests;

public class ChatClientTests
{
    [Theory]
    [InlineData(
        "<thought>some thoughts</thought>國家資通安全法重點摘要",
        "國家資通安全法重點摘要"
    )]
    [InlineData(
        "<think>thought content\nline 2</think>Actual output",
        "Actual output"
    )]
    [InlineData(
        "<thought class=\"xyz\">some thoughts</thought>  Clean text  ",
        "Clean text"
    )]
    [InlineData(
        "<THINK>upper case</THINK>Hello World",
        "Hello World"
    )]
    [InlineData(
        "<thought>one</thought>middle<think>two</think>end",
        "middleend"
    )]
    [InlineData(
        "No tags here",
        "No tags here"
    )]
    [InlineData(
        "<thought>unclosed tags are kept since it might be raw text",
        "<thought>unclosed tags are kept since it might be raw text"
    )]
    public void StripThoughtBlocks_ShouldCleanOutputCorrectly(string input, string expected)
    {
        // Use reflection to invoke the private static method StripThoughtBlocks on ChatClient
        var method = typeof(ChatClient).GetMethod(
            "StripThoughtBlocks",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(method);
        var result = method.Invoke(null, new object[] { input }) as string;
        
        Assert.Equal(expected, result);
    }
}
