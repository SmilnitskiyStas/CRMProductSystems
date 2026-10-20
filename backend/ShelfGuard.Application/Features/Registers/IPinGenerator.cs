using System.Security.Cryptography;

namespace ShelfGuard.Application.Features.Registers;

/// <summary>Source of server-generated POS PINs (abstracted so tests can force collisions).</summary>
public interface IPinGenerator
{
    string Next();
}

public sealed class RandomPinGenerator : IPinGenerator
{
    public const int Length = 6;

    public string Next() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
