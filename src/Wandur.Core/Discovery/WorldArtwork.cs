namespace Wandur.Core.Discovery;

/// <summary>Which picture of a world to fetch.</summary>
public enum WorldArtwork
{
    /// <summary>The world's own choice: an owner's banner, else the generated illustration, else a supplied banner.</summary>
    Preferred,
    /// <summary>The illustration the directory generated from the listing, the one the site shows.</summary>
    Generated,
    /// <summary>The banner the listing supplied.</summary>
    Supplied
}
