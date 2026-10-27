using KSA.Core.Data;

namespace KSA.Core
{
    /// <summary>
    /// Where the agency's state lives between sessions. KSA.KSP implements this with the game's save file.
    /// The host drives the timing (it tells Core when to save and load); this interface only moves the data.
    /// </summary>
    public interface IPersistence
    {
        /// <summary>Returns the previously saved state, or null when there is none (new game, or a save made before KSA).</summary>
        DataNode LoadState();
        void SaveState(DataNode state);
    }
}
