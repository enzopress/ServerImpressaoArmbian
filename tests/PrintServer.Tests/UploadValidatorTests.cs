using System.Text;
using Microsoft.Extensions.Options;
using PrintServer.Web.Printing;

namespace PrintServer.Tests;

public class UploadValidatorTests
{
    private static readonly UploadValidator Validator = new(Options.Create(new PrintServerOptions { MaxUploadMb = 1 }));

    private static UploadValidation Validate(string name, byte[] content, long? length = null) =>
        Validator.Validate(name, length ?? content.Length, new MemoryStream(content));

    [Fact]
    public void Real_pdf_is_accepted()
    {
        var result = Validate("doc.PDF", Encoding.ASCII.GetBytes("%PDF-1.7\n..."));

        Assert.True(result.IsValid);
        Assert.Equal(UploadKind.Pdf, result.Kind);
        Assert.Equal(".pdf", result.Extension);
    }

    [Fact]
    public void Png_and_jpeg_are_images()
    {
        Assert.Equal(UploadKind.Image, Validate("a.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).Kind);
        Assert.Equal(UploadKind.Image, Validate("a.jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0]).Kind);
    }

    [Fact]
    public void Text_without_binary_is_accepted()
    {
        Assert.Equal(UploadKind.Text, Validate("lista.txt", Encoding.UTF8.GetBytes("pão\nleite\n")).Kind);
    }

    [Theory]
    [InlineData("falso.pdf")]
    [InlineData("falso.png")]
    [InlineData("falso.jpg")]
    public void Content_that_does_not_match_extension_is_rejected(string name)
    {
        var result = Validate(name, Encoding.ASCII.GetBytes("MZ\x90\0 executavel"));

        Assert.False(result.IsValid);
        Assert.Contains("não corresponde", result.Error);
    }

    [Fact]
    public void Binary_text_file_is_rejected() => Assert.False(Validate("a.txt", [0x41, 0x00, 0x42]).IsValid);

    [Fact]
    public void Disallowed_extension_is_rejected()
    {
        var result = Validate("script.sh", Encoding.ASCII.GetBytes("#!/bin/sh"));

        Assert.False(result.IsValid);
        Assert.Contains("Tipo não aceito", result.Error);
    }

    [Fact]
    public void Oversized_file_is_rejected()
    {
        var result = Validate("big.pdf", Encoding.ASCII.GetBytes("%PDF-"), length: 2 * 1024 * 1024);

        Assert.False(result.IsValid);
        Assert.Contains("1 MB", result.Error);
    }

    [Fact]
    public void Empty_file_is_rejected() => Assert.False(Validate("a.pdf", []).IsValid);
}
