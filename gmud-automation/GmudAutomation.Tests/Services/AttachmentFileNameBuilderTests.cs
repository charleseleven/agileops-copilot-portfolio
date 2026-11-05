using FluentAssertions;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class AttachmentFileNameBuilderTests
{
    private readonly AttachmentFileNameBuilder _sut = new();

    [Fact]
    public void Build_ArquivoTxt_RetornaPadraoNumeroUsScriptNumeroTaskComExtensaoOriginal()
    {
        var result = _sut.Build(196, 197, "script_original.txt");

        result.Should().Be("US196_SCRIPT_TASK197.txt");
    }

    [Fact]
    public void Build_ArquivoDoc_MantemAExtensaoDoc()
    {
        var result = _sut.Build(201, 202, "instrucoes.doc");

        result.Should().Be("US201_SCRIPT_TASK202.doc");
    }
}
