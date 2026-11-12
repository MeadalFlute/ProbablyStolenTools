namespace ProbablyStolenTools
{
    // Shared by the build-time table generator and runtime traversal. Never inspect these owners.
    internal static class SnapshotTraversalBoundary
    {
        internal static bool IsProtectedOwner(string name)
            => name == "ModLoader" || name == "ModTrust" || name == "ModTrustRecord"
            || name == "ModFingerprint" || name == "ModTrustList" || name == "ModScanner"
            || name == "ModCapability" || name == "ModSecurityDebug" || name == "ModSteamRules"
            || name == "ModSteamGuard" || name == "ModSteamAuth" || name == "ModWorkshop"
            || name == "SteamManager" || name == "PaperBookRegistry" || name == "ModReflect";
    }
}
