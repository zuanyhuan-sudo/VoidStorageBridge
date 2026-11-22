using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria.ModLoader;

namespace VoidStorageBridge.Patches;

/// <summary>
/// Fixes VoidStorage's decompilation artifact: explicit base calls written as ((UIElement)this).X()
/// compile to callvirt, which dispatches back into the subclass override and recurses until the stack dies.
/// Rewriting those callvirt instructions to plain call restores the intended base call.
/// </summary>
internal static class CrashFixPatch
{
	private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	private const string UiElementFullName = "Terraria.UI.UIElement";

	internal static void Apply(Mod mod)
	{
		Assembly asm = ModLoader.GetMod("VoidStorage").Code;
		int patched = 0;
		int failed = 0;
		foreach (Type type in SafeTypes(asm))
		{
			foreach (MethodInfo method in SafeMethods(type))
			{
				if (!IsPatchable(method) || !HasUiElementVirtualCall(method))
				{
					continue;
				}
				try
				{
					MonoModHooks.Modify(method, FixCallvirt);
					patched++;
				}
				catch (Exception e)
				{
					failed++;
					mod.Logger.Warn($"[VoidStorageBridge] crash fix failed on {type.FullName}.{method.Name}: {e.Message}");
				}
			}
		}
		mod.Logger.Info($"[VoidStorageBridge] crash fix applied to {patched} method(s), {failed} failure(s).");
	}

	private static void FixCallvirt(ILContext il)
	{
		foreach (Instruction instr in il.Instrs)
		{
			if (instr.OpCode == OpCodes.Callvirt && instr.Operand is MethodReference mr
				&& mr.DeclaringType != null && mr.DeclaringType.FullName == UiElementFullName)
			{
				instr.OpCode = OpCodes.Call;
			}
		}
	}

	private static bool IsPatchable(MethodInfo method)
	{
		try
		{
			return !method.IsAbstract
				&& !method.ContainsGenericParameters
				&& !method.IsConstructor
				&& method.GetMethodBody() != null;
		}
		catch
		{
			return false;
		}
	}

	private static bool HasUiElementVirtualCall(MethodInfo method)
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
			if (op != 0x6F && op != 0x28)
			{
				continue;
			}
			int token = BitConverter.ToInt32(il, i + 1);
			try
			{
				MethodBase target = method.Module.ResolveMethod(token);
				if (target?.DeclaringType != null && target.DeclaringType.FullName == UiElementFullName)
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

	private static Type[] SafeTypes(Assembly asm)
	{
		try
		{
			return asm.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			return Array.FindAll(e.Types, t => t != null);
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}

	private static MethodInfo[] SafeMethods(Type type)
	{
		try
		{
			return type.GetMethods(AllDeclared);
		}
		catch
		{
			return Array.Empty<MethodInfo>();
		}
	}
}
