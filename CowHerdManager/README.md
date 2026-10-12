# 牛群管理桩（1.6 实验原型）

建造“cow herd manager post”后，默认管理距离设施 30 格内的玩家牛；选中设施可随时关闭。设施会把这些牛的**空闲游荡工作**改成原地等待，每 300 tick 重新检查其他需要。多座设施可以覆盖不同牛群。牛仍真实存在于地图上，未取消原版的进食、产奶、繁殖、健康、照料或危险应对逻辑。

## 实现边界

- 只拦截 `JobGiver_Wander.TryGiveJob`；不跳过 `Pawn.Tick`。因此设计目标是降低无目的走动/寻路，不能宣称把牛的全部 CPU 成本冻结。
- 只作用于玩家派系、已生成、未倒地、非精神状态的 `Cow`。不作用于人、机械体、牦牛、其他动物。
- 设施只读牛的位置和状态，不自动移动牛、不生成牛奶、不修改牛的属性，也不修改原有存档对象。关闭设施或拆除后恢复原版游荡。
- 覆盖范围以直线距离判断，不要求同一围栏。应将设施建在目标牛圈中央；这一版本暂不提供按单头牛勾选。
- 与其他修改动物游荡工作的 Mod 可能冲突。已进行游戏加载与建筑菜单烟雾测试，尚未在有牛的存档中验证行为，也没有 TPS 对照。

## 构建与下一轮验证

在本机游戏 1.6 安装目录和 Harmony Workshop 目录可用时：

```powershell
dotnet build CowHerdManager/Source/CowHerdManager/CowHerdManager.csproj -p:RimWorldDir=F:\SteamLibrary\steamapps\common\RimWorld -p:HarmonyDir=F:\SteamLibrary\steamapps\workshop\content\294100\2009463077
```

构建输出在 `CowHerdManager/Assemblies`。2026-10-12 构建为 0 警告、0 错误；XML 可以解析。用原有 11 Mod 配置加此 Mod 启动游戏，日志出现 `[Cow Herd Manager] Idle-wander patch loaded.`，没有相关错误；B 实验副本成功载入，“杂项”菜单中显示牛群管理桩。B 没有牛，因此**这次并未验证补丁对牛的运行效果**。测试后已退出游戏，并恢复原 Mod 配置；不会修改原存档。

下一轮在 A 的**复制存档**中加载此 Mod，建一座设施并观察：开/关两种状态的牛数、每头母牛产奶、觅食、健康、繁殖，以及相同场景下短时 TPS。若观察到牛停止正常进食或逃险，立即关闭设施并修正补丁。只有开/关对照显示明确差异时，才扩大冻结范围。
