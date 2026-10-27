using CrazyMarket.Api.Domain.Entities;

namespace CrazyMarket.Api.Abstractions;

public record EslRenderResult(
    string Base64Png,
    byte[] PngBytes,
    decimal ActivePrice,
    string? BannerText,
    bool PushedToGateway);

/// <summary>Generování a odesílání 1-bitových náhledů (296×128 px) na E-ink cenovky.</summary>
public interface IEslGateway
{
    Task<EslRenderResult> RenderAndPushTagAsync(Product product, decimal activePrice, CancellationToken ct = default);
}
