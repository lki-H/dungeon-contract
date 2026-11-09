# 架构与三条关键链路

Dungeon Contract把高频战斗与持久化业务分开：Unity权威服务运行战斗模拟，Go服务处理身份、组队、匹配、准入、可靠事实、合同和结算。Go内部按模块组织，以单业务进程部署；Unity权威是独立进程。

```mermaid
flowchart LR
    P["Unity玩家端<br/>输入、预测、渲染"]
    W["Web管理与控制页<br/>React / TypeScript"]
    G["Go业务服务<br/>Gin / WebSocket V2"]
    A["Unity权威服务<br/>30Hz战斗、碰撞、房间状态"]
    M[("MySQL<br/>Run、事实、结果、奖励与账本")]
    R[("Redis<br/>可重建的队列与奖励投影")]
    Q["Worker<br/>待处理操作、通知、周期维护"]
    P -->|"HTTP / WebSocket控制"| G
    W -->|"HTTP / WebSocket"| G
    P <-->|"UDP输入与裁剪后的战斗状态"| A
    A -->|"内部HTTP：身份绑定的事实帧"| G
    G -->|"返回准入、分配与控制命令"| A
    G <-->|"事务与唯一约束"| M
    Q <-->|"有界扫描、条件更新、Outbox"| M
    G --> Q
    G -->|"从MySQL重建投影"| R
    Q -->|"提交后的通知尝试"| G
```

LAN模式额外使用受限玩家网关，向明确允许的IP开放玩家入口；Go内部接口与存储继续限制在本机。Go业务模块共享数据库和事务边界，没有声明为独立部署的微服务。

| 模块 | 负责什么 | 源码入口 |
| --- | --- | --- |
| 身份与传输 | JWT期限、会话、HTTP／WebSocket权限与消息限制 | [auth](../../backend/internal/auth/jwt.go)、[WebSocket传输](../../backend/internal/handler/v2_transport.go) |
| 组队与匹配 | 队伍版本、准备状态、匹配提案、玩家活动占用与Run创建 | [party](../../backend/internal/pve/party/service.go)、[matchmaking](../../backend/internal/pve/matchmaking/service.go) |
| 权威准入 | 内容指纹、分配、票据、来源代次、连接代次与加载确认 | [gameplay/service.go](../../backend/internal/pve/gameplay/service.go) |
| 战斗模拟 | 移动、碰撞、攻击、弹体、机关、陷阱与状态同步 | [Unity入口](../../client/unity-demo/Assets/DungeonContract/Runtime/DcP0V3.cs)、[v8世界](../../client/unity-demo/Assets/DungeonContract/Runtime/DcV8World.cs) |
| 事实与经济 | 事实序列、任务进度、终态结果、奖励唯一性与账本 | [frame](../../backend/internal/pve/gameplay/frame.go)、[settlement](../../backend/internal/pve/settlement/service.go) |
| 恢复与维护 | 幂等操作、有限重试、Outbox通知、Redis投影重建 | [store](../../backend/internal/pve/store/store.go)、[worker](../../backend/internal/pve/worker/worker.go)、[projection](../../backend/internal/pve/projection/service.go) |

## 1. 组队、匹配与版本化入局

```mermaid
sequenceDiagram
    participant C as Unity玩家端
    participant G as Go控制与匹配
    participant D as MySQL
    participant A as Unity权威服务
    C->>G: 选方案/合同、准备、提交排队意图
    G->>D: 校验队伍版本、空闲状态与活动锁
    G-->>C: 匹配提案及revision
    C->>G: 接受提案
    G->>D: 事务创建Run并预留权威分配
    G-->>C: assigned通知（也可查询当前活动）
    A->>G: Claim分配，取得冻结Manifest
    C->>G: 为该Run请求准入票据
    G-->>C: 票据、内容指纹、连接代次
    C->>A: 带票据连接UDP战斗会话
    A->>G: Consume并校验版本/分配/会话
    G-->>A: 玩家槽位和绑定代次
    A->>G: Loaded/Start，确认完整加载成员
    G->>D: Run从loading进入running
    A-->>C: 可靠基线就绪后进入战斗
```

匹配提案确认与权威Claim／玩家票据申请存在异步交接，图表示依赖关系，不要求网络响应严格按显示顺序到达。准入校验操作、内容、世界、试炼和配置指纹，不能靠客户端补默认字段加入不兼容的局。实现见[匹配提案确认](../../backend/internal/pve/matchmaking/service.go)、[分配／Issue／Consume／Start](../../backend/internal/pve/gameplay/service.go)与[Unity控制入口](../../client/unity-demo/Assets/DungeonContract/Runtime/DcP0V3.cs)。

## 2. 权威事实、乱序与重试

```mermaid
sequenceDiagram
    participant A as Unity权威服务
    participant G as Go事实入口
    participant D as MySQL
    A->>G: Run/来源代次/sequence/frame_id/指纹/有界事实
    G->>D: 开启事务并加载Run与分配
    alt 已保存同frame_id且指纹相同
        D-->>G: 返回既有receipt
    else 来源、控制栅栏或内容不合法
        G-->>A: 拒绝，不能推进进度
    else 新合法帧
        G->>D: 保存received帧，受数量/字节/重排窗口限制
        loop 从applied_sequence+1连续消费
            G->>D: 应用事实、推进水位并保存applied回执
        end
    end
    D-->>G: 合法分支提交事务
    G-->>A: received或applied及已应用水位
    opt 回执丢失
        A->>G: 用同身份、同内容重发
        G-->>A: 返回既有回执，避免重复应用
    end
```

接收成功与已经应用是不同状态；缺前帧时不能跨序号推进任务。相同身份但内容不同返回冲突，超过有界窗口的输入也拒绝。高频位移、弹体位置和碰撞仍留在Unity，不逐帧写入MySQL。代码与Schema见[事实入口](../../backend/internal/pve/gameplay/frame.go)、[当前帧契约](../game-demo/contracts/frame-v8-p5.schema.json)与[运行状态契约](../game-demo/contracts/runtime-v8/README.md)。

## 3. 终态、结算与返回大厅

```mermaid
sequenceDiagram
    participant A as Unity权威服务
    participant G as Go事实与结算
    participant D as MySQL
    participant W as Worker
    participant C as Unity玩家端/大厅
    A->>G: 合法终态、截止帧及汇总
    G->>D: 事务保存终态，排入settlement操作
    W->>D: 读取到期待处理操作
    W->>G: Settle(Run)
    loop 每个参与者
        G->>D: 事务校验结果、奖励唯一键、资产行锁
        G->>D: 写奖励+余额+账本+参与者结果+Outbox
    end
    G->>D: 全员结果完成后关闭Run、释放队伍状态
    W->>D: 确认选中的操作尝试，处理Outbox
    W-->>C: 提交后尝试推送结果通知
    C->>G: 查询活动/结果，恢复遗漏通知
    C->>C: 停止旧战斗连接，再恢复大厅控制连接
    opt 主动退出尚未确认
        C->>G: v2.run.leave，等待确认
    end
    C->>G: 重新查询大厅状态，选择后续组队/排队
```

Outbox在业务事务内落库，推送发生在发布状态提交后；进程在两者之间失败时，客户端依靠查询恢复，不能描述成“客户端恰好接收一次”。结算见[SettleParticipant／FinalizeRun](../../backend/internal/pve/settlement/service.go)，重试见[Worker](../../backend/internal/pve/worker/worker.go)，返厅顺序见[DcP5Lobby.ReturnFromRun](../../client/unity-demo/Assets/DungeonContract/Runtime/DcP5Lobby.cs)。

## 部署与故障边界

当前Go进程持有数据库所有权锁，同库再启动一个进程不构成水平扩容。Unity是高频战斗信任根，Go验证业务所需的有限事实，不重新模拟每次碰撞。业务确认不可用可触发受控技术等待，两个进程不是完全独立的故障域。跨进程战斗崩溃恢复、多实例容错、公网部署及商业SLO需要另行设计和验证。

验证记录见[当前状态](current-status.md)、[11月10日隔离存储与竞态检查](review-20261110.md)及[威胁模型](game-realtime-gm-demo-threat-model.md)。
