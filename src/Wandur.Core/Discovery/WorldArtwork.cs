namespace Wandur.Core.Discovery;

/// <summary>Which picture of a world to fetch.</summary>
public enum WorldArtwork
{
    /// <summary>The supplied banner when the listing has one, else the generated illustration.</summary>
    Preferred,
    /// <summary>The illustration the directory generated from the listing, the one the site shows.</summary>
    Generated,
    /// <summary>The banner the listing supplied.</summary>
    Supplied
}
