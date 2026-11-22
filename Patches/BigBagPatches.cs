using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ModLoader;
using VoidStorageBridge.Common;

namespace VoidStorageBridge.Patches;

/// <summary>
/// Lets ImproveGame's Super Vault (大背包) feed VoidStorage's crafting UI:
/// availability count, real consumption, crafting pool and cache fingerprint.
/// Every patch is idempotent: methods that already reference a BigBagContents bridge are skipped.
/// </summary>
internal static class BigBagPatches
{
	private const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	internal static void Apply(Mod mod)
	{
		if (!ModLoader.HasMod("ImproveGame"))
		{
			mod.Logger.Warn("[VoidStorageBridge] ImproveGame is not installed; big bag bridging disabled.");
			return;
		}
		Assembly asm = ModLoader.GetMod("VoidStorage").Code;
		Type storage = asm.GetType("VoidStorage.Data.NetworkStorage");
		Type executor = asm.GetType("VoidStorage.Features.CraftExecutor");

		int ok = 0;
		ok += Try(mod, "GetAvailable", () => PatchGetAvailable(Find(storage, "GetAvailable", typeof(int))));
		ok += Try(mod, "ConsumeForMaterial", () => PatchConsume(Find(storage, "ConsumeForMaterial", typeof(int), typeof(int))));
		ok += Try(mod, "BagFingerprint", () => PatchFingerprint(Find(storage, "BagFingerprint")));
		ok += Try(mod, "BuildPool", () => PatchBuildPool(Find(executor, "BuildPool")));
		mod.Logger.Info($"[VoidStorageBridge] big bag patches active: {ok}/4.");
	}

	private static int Try(Mod mod, string label, Action action)
	{
		try
		{
			action();
			return 1;
		}
		catch (Exception e)
		{
			mod.Logger.Warn($"[VoidStorageBridge] bridge patch '{label}' skipped: {e.Message}");
			return 0;
		}
	}

	private static MethodInfo Find(Type type, string name, params Type[] args)
	{
		if (type == null)
		{
			throw new InvalidOperationException($"type missing for {name}");
		}
		MethodInfo method = args.Length == 0
			? type.GetMethod(name, StaticAll)
			: type.GetMethod(name, StaticAll, null, args, null);
		if (method == null)
		{
			throw new MissingMethodException($"{type.FullName}.{name} not found");
		}
		if (IsBridged(method))
		{
			throw new InvalidOperationException($"{name} already bridged");
		}
		return method;
	}

	/// <summary>True when the method already calls into any BigBagContents bridge (idempotency guard).</summary>
	private static bool IsBridged(MethodInfo method)
	{
		byte[] il;
		try
		{
			il = method.GetMethodBody()?.GetILAsByteArray();
		}
		catch
		{
			return false;
		}
		if (il == null)
		{
			return false;
		}
		for (int i = 0; i + 4 < il.Length; i++)
		{
			byte op = il[i];
			if (op != 0x28 && op != 0x6F)
			{
				continue;
			}
			try
			{
				MethodBase target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
				if (target?.DeclaringType?.Name != null && target.DeclaringType.Name.Contains("BigBagContents"))
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private static MethodInfo LocalPlayerGetter()
	{
		return typeof(Main).GetProperty(nameof(Main.LocalPlayer), BindingFlags.Public | BindingFlags.Static)?.GetGetMethod()
			?? throw new MissingMethodException("Main.LocalPlayer");
	}

	/// <summary>int GetAvailable(int type) => original + big bag count.</summary>
	private static void PatchGetAvailable(MethodInfo method)
	{
		MethodInfo localPlayer = LocalPlayerGetter();
		MethodInfo count = typeof(BigBagContents).GetMethod(nameof(BigBagContents.Count))
			?? throw new MissingMethodException("BigBagContents.Count");
		MonoModHooks.Modify(method, il =>
		{
			IList<Instruction> instrs = il.Instrs;
			for (int i = instrs.Count - 1; i >= 0; i--)
			{
				if (instrs[i].OpCode != OpCodes.Ret)
				{
					continue;
				}
				instrs.Insert(i, Instruction.Create(OpCodes.Add));
				instrs.Insert(i, Instruction.Create(OpCodes.Call, il.Import(count)));
				instrs.Insert(i, Instruction.Create(OpCodes.Ldarg_0));
				instrs.Insert(i, Instruction.Create(OpCodes.Call, il.Import(localPlayer)));
			}
		});
	}

	/// <summary>void ConsumeForMaterial(int type, int count): take from the big bag first, then let the original logic take the rest.</summary>
	private static void PatchConsume(MethodInfo method)
	{
		MethodInfo localPlayer = LocalPlayerGetter();
		MethodInfo consume = typeof(BigBagContents).GetMethod(nameof(BigBagContents.Consume))
			?? throw new MissingMethodException("BigBagContents.Consume");
		MonoModHooks.Modify(method, il =>
		{
			VariableDefinition taken = new VariableDefinition(il.Import(typeof(int)));
			il.Body.Variables.Add(taken);
			ParameterDefinition countArg = il.Body.Method.Parameters[1];
			Instruction[] seq =
			{
				Instruction.Create(OpCodes.Call, il.Import(localPlayer)),
				Instruction.Create(OpCodes.Ldarg_0),
				Instruction.Create(OpCodes.Ldarg_1),
				Instruction.Create(OpCodes.Call, il.Import(consume)),
				Instruction.Create(OpCodes.Stloc, taken),
				Instruction.Create(OpCodes.Ldarg_1),
				Instruction.Create(OpCodes.Ldloc, taken),
				Instruction.Create(OpCodes.Sub),
				Instruction.Create(OpCodes.Starg, countArg),
			};
			IList<Instruction> instrs = il.Instrs;
			for (int i = seq.Length - 1; i >= 0; i--)
			{
				instrs.Insert(0, seq[i]);
			}
		});
	}

	/// <summary>private static int BagFingerprint() => original * 31 + big bag footprint.</summary>
	private static void PatchFingerprint(MethodInfo method)
	{
		MethodInfo localPlayer = LocalPlayerGetter();
		MethodInfo footprint = typeof(BigBagContents).GetMethod(nameof(BigBagContents.Footprint))
			?? throw new MissingMethodException("BigBagContents.Footprint");
		MonoModHooks.Modify(method, il =>
		{
			IList<Instruction> instrs = il.Instrs;
			for (int i = instrs.Count - 1; i >= 0; i--)
			{
				if (instrs[i].OpCode != OpCodes.Ret)
				{
					continue;
				}
				instrs.Insert(i, Instruction.Create(OpCodes.Add));
				instrs.Insert(i, Instruction.Create(OpCodes.Call, il.Import(footprint)));
				instrs.Insert(i, Instruction.Create(OpCodes.Call, il.Import(localPlayer)));
			}
		});
	}

	/// <summary>private static Dictionary&lt;int,int&gt; BuildPool(): merge the big bag into the returned pool.</summary>
	private static void PatchBuildPool(MethodInfo method)
	{
		MethodInfo merge = typeof(BigBagContents).GetMethod(nameof(BigBagContents.MergeInto))
			?? throw new MissingMethodException("BigBagContents.MergeInto");
		MonoModHooks.Modify(method, il =>
		{
			IList<Instruction> instrs = il.Instrs;
			for (int i = instrs.Count - 1; i >= 0; i--)
			{
				if (instrs[i].OpCode == OpCodes.Ret)
				{
					instrs.Insert(i, Instruction.Create(OpCodes.Call, il.Import(merge)));
				}
			}
		});
	}
}
