# VoidStorage Bridge

让 **VoidStorage（虚空仓库）** 与 **ImproveGame（更好的体验）** 的材料双向互通，同时修复 VoidStorage 打开界面卡死的 bug。

一个**独立补丁 MOD**：不修改、不覆盖、不包含原 MOD 的任何文件，装上即可生效。

---

## 功能

| # | 功能 | 说明 |
|---|------|------|
| 1 | **修复打开虚空仓库卡死** | 修复反编译伪影导致的无限递归爆栈（原版打开面板即定格） |
| 2 | **虚空仓库材料 → 原版工作台** | 仓库里的材料直接参与原版合成（工作台/铁砧/熔炉…），合成后仓库数量同步扣除 |
| 3 | **大背包材料 → 虚空工作台** | ImproveGame 大背包（超级保险库）里的材料可用于虚空仓库面板的合成，扣料同步 |
| 4 | **尊重 ImproveGame 开关** | 大背包「参与合成」开关关闭时，本补丁自动不参与 |

## 原理

### 1. 卡死修复（核心）
VoidStorage 是闭源 MOD，其发布版由反编译器重新编译而来。源码里的显式基类调用 `((UIElement)this).Update()` 被编译成 `callvirt`（虚分派），运行时又派发回子类自己的 `override`，形成无限递归：

```
RBRMainPanel.Update -> callvirt UIElement.Update -> RBRMainPanel.Update -> ... 爆栈
```

补丁在运行时用 MonoMod 的 IL 钩子，把 VoidStorage 程序集里所有指向 `Terraria.UI.UIElement` 虚方法的 `callvirt` 改写为 `call`（非虚基类调用），递归炸弹消除。只处理真正含有该指令的方法，开销可忽略。

### 2. 大背包 → 虚空工作台
VoidStorage 的合成判定与扣料完全走它自己的管线（不经过原版 `Recipe`），因此只需在四个位置插入桥接：

| 目标方法 | 补丁行为 |
|----------|----------|
| `NetworkStorage.GetAvailable(int)` | 返回值加上大背包同物品数量 |
| `NetworkStorage.ConsumeForMaterial(int,int)` | 先从大背包扣料，剩余部分交回原逻辑 |
| `CraftExecutor.BuildPool()` | 把大背包物品并入合成材料池 |
| `NetworkStorage.BagFingerprint()` | 指纹纳入大背包，保证缓存及时刷新 |

大背包通过 ImproveGame 官方跨 MOD 接口 `Call("GetBigBagItems", player)` 获取，返回的是**活引用**；ImproveGame 客户端每 tick 轮询并自动同步，因此直接改 `stack` 是安全的。

### 3. 虚空仓库 → 原版工作台
通过 `ModPlayer.AddMaterialsForCrafting` 把仓库物品以克隆形式交给原版合成系统，并在消耗回调里按差值调用 `NetworkStorage.Withdraw` 扣回仓库。

## 安装

1. 安装 [VoidStorage](https://steamcommunity.com/sharedfiles/filedetails/?id=3804410902) 与 [ImproveGame](https://steamcommunity.com/sharedfiles/filedetails/?id=2797518634)
2. 把 `VoidStorageBridge.tmod` 放进 `<文档>/My Games/Terraria/tModLoader/Mods/`
3. 启用三个 MOD，重载

## 兼容性

- tModLoader **2026.9**（1.4.4 分支）
- 面向**官方发布版** VoidStorage。若你使用自行修改/重编译过的 VoidStorage（例如已经内置了大背包桥接），补丁会自动检测并跳过对应挂钩，不会重复计算。
- 联机：三个 MOD 都需装在同一侧（客户端/服务器规则遵循各 MOD 自身要求）。

## 从源码构建

```bash
# 把本目录放进 tModLoader/ModSources/，然后：
cd "<tModLoader 安装目录>"
dotnet tModLoader.dll -server -build "<ModSources>/VoidStorageBridge"
```

产物：`tModLoader/Mods/VoidStorageBridge.tmod`

## 免责声明

本项目**不包含** VoidStorage 或 ImproveGame 的任何源代码/资源，仅在运行时通过公开 API 与 IL 钩子进行互操作。请支持原作者，前往创意工坊订阅正版。

## License

MIT
