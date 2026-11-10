using QrLabelPrinter.Services;
using Xunit;

namespace QrLabelPrinter.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Parses_all_three_parameters()
    {
        var r = CommandLineParser.Parse(TestData.Args("MMUSTERMANN", "Max", "Mustermann"));
        Assert.True(r.IsValid);
        Assert.Equal("MMUSTERMANN", r.User!.Username);
        Assert.Equal("Max", r.User.FirstName);
        Assert.Equal("Mustermann", r.User.LastName);
    }

    [Fact]
    public void Parses_equals_syntax_and_ignores_unknown_arguments()
    {
        var r = CommandLineParser.Parse(["--foo", "bar", "--sap-user=ABC", "--firstname=Max", "--lastname=Muster"]);
        Assert.True(r.IsValid);
        Assert.Equal("ABC", r.User!.Username);
    }

    [Theory]
    [InlineData("Hans Peter", "von der Heide")]
    [InlineData("Jürgen", "Müller-Lüdenscheidt")]
    [InlineData("Zoë", "O'Brien")]
    [InlineData("Åsa", "Straße")]
    [InlineData("Anne-Marie", "\"Quote\" ^FS ~ _ \\")]
    public void Keeps_spaces_umlauts_and_special_characters(string first, string last)
    {
        var r = CommandLineParser.Parse(TestData.Args(first: first, last: last));
        Assert.True(r.IsValid);
        Assert.Equal(first, r.User!.FirstName);
        Assert.Equal(last, r.User.LastName);
    }

    [Fact]
    public void Normalizes_decomposed_unicode_to_composed_form()
    {
        string decomposed = "Müller"; // u + kombinierendes Trema
        var r = CommandLineParser.Parse(TestData.Args(last: decomposed));
        Assert.Equal("Müller", r.User!.LastName);
    }

    [Fact]
    public void Label_username_is_uppercase_but_input_is_unchanged()
    {
        var r = CommandLineParser.Parse(TestData.Args(user: "mmustermann"));
        Assert.Equal("mmustermann", r.User!.Username);
        Assert.Equal("MMUSTERMANN", r.User.LabelUsername);
    }

    [Theory]
    [InlineData("--sap-user")]
    [InlineData("--firstname")]
    [InlineData("--lastname")]
    public void Missing_parameter_is_an_error(string missing)
    {
        var args = TestData.Args().ToList();
        int i = args.IndexOf(missing);
        args.RemoveRange(i, 2);

        var r = CommandLineParser.Parse(args);
        Assert.False(r.IsValid);
        Assert.Null(r.User);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void No_arguments_is_an_error()
    {
        var r = CommandLineParser.Parse([]);
        Assert.False(r.IsValid);
        Assert.Equal(3, r.Errors.Count);
    }

    [Fact]
    public void Option_without_value_is_an_error()
    {
        var r = CommandLineParser.Parse(["--sap-user", "--firstname", "Max", "--lastname", "Muster"]);
        Assert.False(r.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TOOLONGUSERNAME1")]
    [InlineData("MAX MUSTER")]
    [InlineData("MÜLLER")]
    public void Invalid_username_is_an_error(string username)
    {
        var r = CommandLineParser.Parse(TestData.Args(user: username));
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Control_characters_or_empty_names_are_errors()
    {
        Assert.False(CommandLineParser.Parse(TestData.Args(first: "Ma\nx")).IsValid);
        Assert.False(CommandLineParser.Parse(TestData.Args(last: "")).IsValid);
        Assert.False(CommandLineParser.Parse(TestData.Args(first: new string('a', 41))).IsValid);
    }
}
