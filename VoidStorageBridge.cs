using VoidStorageBridge.Patches;
using Terraria.ModLoader;

namespace VoidStorageBridge;

public class VoidStorageBridge : Mod
{
	public override void Load()
	{
		if (!ModLoader.HasMod("VoidStorage"))
		{
			Logger.Warn("[VoidStorageBridge] VoidStorage is not installed; bridge is disabled.");
			return;
		}

		CrashFixPatch.Apply(this);
		BigBagPatches.Apply(this);
	}
}
