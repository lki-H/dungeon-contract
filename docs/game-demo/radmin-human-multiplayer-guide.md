# 从零开始：Dungeon Contract v8 局域网／Radmin 联机

适用设计2.5、`dc_ruins.v8.p5.1`、战斗协议8、控制 `/ws` V2。本文只有当前v8入口；旧v4—v7启动器和包不能混用。当前玩家操作见[v8玩家说明](v8-player-guide.md)。

用户已反馈最新既有包完成真人联机、整局和返厅续局，未发现重大问题。这不扩展为未经记录的人数、电脑数量、网络或性能结果。本文是操作教程，完成其中TCP检查不等于战斗UDP或全流程通过。

## 一、先分清谁做什么

| 身份 | 需要准备 | 启动入口 |
| --- | --- | --- |
| 主机电脑 | 完整源码／主机交付、健康的Demo MySQL和Redis、同批LAN构建、PowerShell7；源码构建另需Go和可用Unity许可 | `start-v8-lan-host.cmd` |
| 每位玩家，包括主机本人 | 同一批完整Windows玩家文件夹 | 包内 `start-v8-player.cmd` |
| 不同地点的电脑 | 各自安装Radmin VPN并加入同一个私人网络 | 保持VPN运行 |
| 同一真实局域网的电脑 | 使用各自实际LAN IPv4，可以不安装VPN | 其他步骤相同 |

朋友不需要安装Go、Unity编辑器、Docker或你的数据库。主机窗口只启动服务；主机本人玩游戏还要单独开一个玩家窗口。

有三类独立密码：Radmin网络密码用于加入私人网络；游戏账号密码用于登录；数据库／服务密钥由工具保存在主机外部private目录。后两者不能用VPN密码替代，任何一类都不要提交到GitHub或发送整个private目录。

下面 `26.10.20.30`、`26.40.50.60` 均为示例，要替换为各自实际地址。源码目录和缓存目录均可自选；不要照抄原作者的E/D盘路径。

## 二、安装Radmin VPN：你和朋友分别做一次

### 2.1 找到正确软件

1. 打开浏览器，在地址栏输入：<https://www.radmin-vpn.com/cn/>。
2. 看清域名是 `radmin-vpn.com`，页面产品是 **Radmin VPN**。
3. 点击“免费下载”。
4. 下载完成后，右键安装文件→“属性”→“数字签名”（若系统提供此页），核对发布者与官网的Famatech信息；如果来源/签名异常，停止，不从第三方下载站替换。
5. 双击安装文件。Windows询问“是否允许此应用对设备进行更改”时，仅在你确认刚下载的是官方安装程序后选择“是”。
6. 按安装向导完成；出现安装虚拟网络适配器/驱动的提示属于这款VPN的组成部分，核对发布者后继续。
7. 从开始菜单搜索并打开“Radmin VPN”。

**预期结果：** 出现Radmin VPN主窗口，上方能看到本机名称和一个IPv4地址。常见是 `26.x.x.x`。

不要点“帮助→安装Radmin Server”。那是远程控制软件，本游戏不需要，不要为联机给朋友“完全控制电脑”的权限。

安装后若提示“服务未启动”，先点主窗口的圆形电源按钮启动。若始终“等待网络适配器的响应”，先重启Windows再开软件；仍失败走官方支持，不直接照网上教程卸驱动或关闭全部安全软件。

### 2.2 如果你的界面是英文

常见名称对应：

| 英文 | 中文 |
| --- | --- |
| Network | 网络 |
| Create network | 创建网络 |
| Join network | 加入网络 |
| Network name | 网络名称 |
| Password | 密码 |
| Ping | Ping/测试连接 |
| Relay/TCP | 中继/TCP |

界面文字随版本可能略有变化，以官网当前版本为准，不需要必须和截图逐像素一致。

## 三、你创建私人网络，朋友加入

### 3.1 你在主机上创建

1. 打开Radmin VPN。
2. 点击“创建网络”；有些布局在“网络”菜单里。
3. “网络名称”填一个本轮专用、较难撞名的名称，例如 `DC-Test-日期-随机字符`，不要照抄示例当固定公共网络。
4. 设置独立、较长的网络密码，不要用常用邮箱/微信/银行卡密码；不要在本文或项目里保存真实密码。
5. 点击“创建”。
6. 看到该网络出现在列表中。
7. 私下把**网络名称、网络密码**发给受邀朋友。

使用自己的私人网络，不加入Radmin的公开游戏网络。加入公开网络会改变谁能接触你的虚拟网卡，不适合本次小范围测试。

### 3.2 每位朋友在自己电脑加入

1. 打开Radmin VPN。
2. 点击“加入网络”。
3. 输入你发来的网络名称和网络密码，大小写/空格保持一致。
4. 点击“加入”。
5. 在网络成员列表中找到你的电脑。
6. 保持Radmin VPN运行，开游戏期间不要关闭它。

**预期结果：** 你和朋友都能在同一个网络下看到对方。电脑名称可能不是昵称，先语音确认谁是谁。

### 3.3 把地址写成一张小表

在Radmin主窗口看**各自电脑的IPv4**，不是百度“我的IP”，不是Wi-Fi地址，不是 `127.0.0.1`，也不是朋友那边的“主机地址”。

以下全部是示例，你必须用自己的真实值替换：

| 人员 | 示例地址 | 用途 |
| --- | --- | --- |
| 你/主机 | `26.10.20.30` | 所有玩家填入的服务器地址 |
| 朋友甲 | `26.40.50.60` | 主机允许列表与防火墙的远端范围 |
| 朋友乙（若有） | `26.70.80.90` | 同上 |
| 朋友丙（若有） | `26.100.110.120` | 同上 |

你自己玩时也填写 `26.10.20.30`，本轮不要填 `127.0.0.1`。主机服务和玩家网关绑定的是指定Radmin地址。

### 3.4 先测试Radmin连接

1. 你右键Radmin成员列表中的朋友电脑→Ping。
2. 朋友也右键你的电脑→Ping。
3. 看是否出现连续回复、延迟和丢包信息。保存20次左右的观察结果；可稍后在终端运行：
   ```powershell
   ping -n 20 26.10.20.30
   ```
4. 如果Radmin显示对方离线，先解决Radmin，不开游戏反复重试。

Ping不通不必立即断言VPN坏了：ICMP可能被防火墙拦截。后面的TCP `Test-NetConnection` 和游戏UDP验证更有针对性。反过来，Ping通也不代表游戏端口一定通。

若显示“中继/TCP”，官方解释是未建立直接连接后的回退。仍可先尝试连通，但记录这一状态；明显延迟/丢包时不拿它证明“稳定局域网手感已达标”。不为本次测试购买公网IP或关闭安全软件。


## 四、主机准备当前构建

### 4.1 已有正常试玩环境

1. 打开Docker Desktop，等待Linux引擎运行。
2. 打开PowerShell7，进入你的源码根目录（包含README和deploy目录）。
3. 使用原private目录，不要为已有数据库重新生成密码：

```powershell
./deploy/portfolio.ps1 -Action ServicesStart
./deploy/portfolio.ps1 -Action CheckServices
```

预检应显示专用数据库 `game_realtime_dc_demo`、Redis认证成功、迁移校验无dirty。若失败，先检查目录、账号和容器日志，不删除卷、重置Docker或覆盖SQL。

### 4.2 第一次从源码准备

先安装项目锁定的Go工具链、PowerShell7、Docker Desktop、Unity `6000.3.25f1` 和所需Windows构建支持，确认Unity许可可用。打开Docker引擎后执行：

```powershell
./deploy/portfolio.ps1 -Action PrepareUnity
./deploy/portfolio.ps1 -Action ServicesStart
./deploy/portfolio.ps1 -Action Initialize
./deploy/portfolio.ps1 -Action CheckServices
```

`PrepareUnity`准备外部缓存、独立随机凭据及Unity Library链接；`Initialize`按校验过的迁移顺序准备新库，不是重置命令。更多后端说明在源码 `docs/portfolio/backend-quickstart.md`。自定义 `DC_CACHE_ROOT`、`DC_PRIVATE_ROOT` 或 `DC_UNITY_PATH` 后，后续终端也必须使用同一组值。

### 4.3 有源码变更时构建LAN主机与玩家包

```powershell
./deploy/dc-v8-lan.ps1 -Action Build -Runtime v8 -P5
```

该命令会构建Unity角色与Go工具，输出完整玩家目录。内存不足时启动器会拒绝开始；按提示保留结果，腾出资源后再做，不能降低保护线或反复重试。普通Unity构建、数据库集成、多个图形客户端不要同时启动。

仅试玩已有且与当前源码匹配的包时，不需要每次执行GoBuild或重新构建。若提示构建绑定失败，说明现有产物与源码／配置不一致，应重新构建，不能修改回执来假装一致。

在源码根目录查询当前路径，不读取或显示凭据：

```powershell
. ./deploy/dc-common.ps1
$paths = Get-DcPaths
$hostBuild = Join-Path $paths.Cache 'builds/v8-p5-lan'
$packagePointer = Join-Path $hostBuild 'player-package.json'
$playerPackage = (Get-Content -LiteralPath $packagePointer -Raw | ConvertFrom-Json).path
$hostBuild
$playerPackage
```

把 `$playerPackage` 指向的整个目录压缩发送给朋友。朋友先完整解压，再运行启动器；不要只复制exe或cmd，不要发送主机服务、源码private、数据库或账户文件。所有人使用同一批包，修改过源码后不要继续混用旧包。

## 五、主机手动配置受限防火墙

本节需要你本人打开管理员PowerShell。项目工具不会代改VPN或防火墙。首次执行前填实际地址和刚查到的实际exe路径；不要关闭整个防火墙。

```powershell
# 所有值均为示例，请替换。RemoteAddress需要包含主机本人及受邀玩家。
$hostIPv4 = '26.10.20.30'
$playerIPv4s = @('26.10.20.30','26.40.50.60')
$gatewayExe = '填写实际hostBuild路径/dc-lan-gateway.exe'
$battleExe = '填写实际hostBuild路径/GameService/GameService.exe'

if (!(Test-Path -LiteralPath $gatewayExe -PathType Leaf) -or
    !(Test-Path -LiteralPath $battleExe -PathType Leaf)) {
    throw '请先填写当前LAN构建中的两个真实程序路径。'
}

New-NetFirewallRule -DisplayName 'DC-V8-LAN-TCP' -Direction Inbound -Action Allow `
    -Program $gatewayExe -Protocol TCP -LocalPort 12880 `
    -LocalAddress $hostIPv4 -RemoteAddress $playerIPv4s -Profile Any

New-NetFirewallRule -DisplayName 'DC-V8-LAN-UDP' -Direction Inbound -Action Allow `
    -Program $battleExe -Protocol UDP -LocalPort 12777 `
    -LocalAddress $hostIPv4 -RemoteAddress $playerIPv4s -Profile Any
```

一套规则对应实际程序路径、主机网卡地址和玩家列表。换包路径或玩家IP后更新相应规则，不通过扩大到所有地址解决问题。数据库23316、Redis26389及内部控制12890只在loopback使用，不给朋友放行。没有必要设置路由器公网端口映射。

## 六、启动主机

回到源码根目录的普通PowerShell7终端：

```powershell
./start-v8-lan-host.cmd
```

1. `This computer Radmin/LAN IPv4`：填主机自己的Radmin／LAN IPv4。
2. `Allowed player IPv4s, comma separated, including yourself`：填所有受邀玩家的实际IPv4，包括主机本人，用英文逗号分隔。
3. 保持终端、Docker和VPN运行，等待服务就绪及本轮日志目录输出。

直接传参数的等价命令：

```powershell
./deploy/dc-v8-lan.ps1 -Action Host -Runtime v8 -P5 `
    -BindAddress '26.10.20.30' `
    -AllowedPlayers @('26.10.20.30','26.40.50.60')
```

如果地址不属于主机网卡、端口已占用、构建不一致或存储未准备好，停止处理报错，不再同时开第二个主机。

## 七、朋友检查连接并开游戏

每位朋友在自己的PowerShell里运行（替换主机IP）：

```powershell
Test-NetConnection -ComputerName 26.10.20.30 -Port 12880
```

`TcpTestSucceeded : True`只表示TCP可达。HTTP登录成功还能验证业务通路，真正进入同一局并移动交战才能覆盖战斗UDP。

双方打开完整玩家目录，运行 `start-v8-player.cmd`，服务器地址都填主机的实际IP。主机本人同样填写该Radmin／LAN IP，不随意改成localhost。不要从源码根目录直接运行脱离完整玩家文件夹的player脚本。

## 八、一起完成游戏流程

1. 各自在Unity大厅注册／登录游戏账号，不交换密码。
2. 添加好友并组队，接受邀请；确认双方在同一队伍。
3. 各自选角色方案及个人合同，成员准备，队长发起匹配，按提示确认。
4. 入局后确认看到彼此、同一房间目标和相同进度。至少实际移动、攻击和使用技能，不以“登录成功”当作联机完成。
5. 正常完成或结束本局，等待结果可靠确认后返回大厅；同一主机支持再开一局，不沿用旧P4“一局后必须重启”的做法。

WASD、鼠标、左键普攻、锤右键蓄力、Q／滚轮换武器、Space、E／F、J交互救援等详见[v8玩家说明](v8-player-guide.md)。多人和试炼打开Esc不会暂停其他人的世界。

本次复核不恢复用户已取消的正式性能、七项自动通关要求和26项重测。若要记录新试玩，使用[联机记录模板](templates/lan-human-playtest-record.md)；实际日期、包清单哈希、人数、设备和网络只填发生过的内容。无需为了补记录重新执行已经完成的全部测试。

## 九、结束与排障

玩家用游戏菜单退出，等待程序正常结束；主机按终端提示结束服务，或使用其支持的正常退出键。结束后如需停止数据库：

```powershell
./deploy/portfolio.ps1 -Action ServicesStop
```

停止容器保留卷，不用 `down -v`、Docker恢复出厂或删除private来结束试玩。

| 现象 | 先检查 |
| --- | --- |
| 启动前说Docker不可用 | Docker Desktop的Linux引擎是否已就绪 |
| Go提示downloading | 缺失依赖在补下载；看最终退出码，下载行本身不是报错 |
| `FAIL_BUILD_BINDING` | 源码／配置变过，重新构建同批主机与玩家包 |
| 地址不属于本机 | Radmin已启动、主机填的是自己的VPN／LAN地址 |
| TCP失败 | 主机正在运行、双方地址、主机允许列表、精确程序及端口规则 |
| 登录成功但入局失败 | UDP12777程序规则、包版本、主机service日志及错误原因 |
| 返回大厅后异常 | 保留当前日志与复现步骤，不通过删账号／数据库反复重试 |
| 内存保护拒绝 | 本次未开重任务；先处理可用资源，不降保护线 |

玩家日志位于 `%LOCALAPPDATA%/DungeonContract/V8/logs`。主机日志位于当前缓存的 `evidence/v8-p5-lan-host-时间/`，具体以启动器输出为准。发问题报告先脱敏，不发送token、密码、完整private或数据库备份。

不用Radmin时只把所有相关示例替换为各自真实局域网IPv4，其余相同。测试完成后，可在管理员终端只删除本次命名规则，并退出／删除本次VPN私人网络：

```powershell
Remove-NetFirewallRule -DisplayName 'DC-V8-LAN-TCP'
Remove-NetFirewallRule -DisplayName 'DC-V8-LAN-UDP'
```

## 参考与边界

VPN安装／建网步骤延续此前核对的厂商流程，界面文案可能随版本变化；只从官方Radmin VPN网站下载安装，不需要Radmin Server远程控制软件。网络保密与主机安全不由一次游戏连通测试证明。公开源码仍是本机／受限LAN Demo，不是公网部署方案。

- Radmin VPN官网与帮助：`https://www.radmin-vpn.com/cn/`、`https://www.radmin-vpn.com/cn/help/`。
- Windows防火墙参数：`https://learn.microsoft.com/powershell/module/netsecurity/new-netfirewallrule`。
- TCP测试说明：`https://learn.microsoft.com/powershell/module/nettcpip/test-netconnection`。
