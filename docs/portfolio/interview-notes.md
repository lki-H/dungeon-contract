# 面试讲解提纲

这份提纲帮助解释真实设计，不代表已经掌握全部实现。面试前按源码亲自走一遍关键路径，如实说明AI辅助开发及自己的需求决策、验证和定位工作；不虚构团队、商业用户或工作履历。

## 30秒介绍

Dungeon Contract是一款Unity合作地下城Demo。高频战斗放在独立Unity权威进程，Go负责身份、组队匹配、版本化准入、可靠事实、合同与结算。重点处理断线和重复交付下的一致性：来源／连接代次隔离旧消息，事实按水位应用，事务、唯一奖励来源和账本支持结算重试。当前提供Windows本机／受限LAN试玩，源码包含隔离存储检查和构建来源审计。

## 选讲三个问题

| 问题 | 设计与实现 | 证据 |
| --- | --- | --- |
| 回执丢失后重发会多记一次吗？ | 稳定ID+内容指纹，同内容返原结果，异内容拒绝；帧连续后应用 | [store.Command](../../backend/internal/pve/store/store.go)、[Frame](../../backend/internal/pve/gameplay/frame.go)、[存储测试](../../backend/internal/pve/gameplay/v8_integration_test.go) |
| 结算一半崩溃怎么不重复发奖？ | 每参与者独立事务、资产行锁和唯一奖励来源；补未完成成员，全员完成再关闭Run | [settlement](../../backend/internal/pve/settlement/service.go)、[Worker回归](../../backend/internal/pve/gameplay/v8_worker_integration_test.go) |
| 旧客户端／旧权威还能影响当前局吗？ | 绑定Run、分配、boot、来源和连接代次、内容指纹 | [Issue／Consume](../../backend/internal/pve/gameplay/service.go)、[事实入口](../../backend/internal/pve/gameplay/frame.go) |

## 常见追问

**为什么不让Go处理每颗子弹？** Unity复用同一引擎的碰撞与战斗逻辑，Go只持久化Run、合同与结算需要的有限事实，避免数据库延迟进入逐帧碰撞路径。代价是Unity成为战斗信任根，Go不独立重算全部命中。

**为什么不拆微服务？** 当前场景用单进程模块和共享事务便于控制一致性。独立Unity进程不意味着每个业务模块都要拆分。同库所有权锁还限制Go单实例；拆分前需重新设计事务、故障域和运维。

**Redis丢了会丢奖励吗？** 奖励、资产与账本在MySQL事务里；Redis的队列／奖励投影可重建。缓存重建不意味着实时弹体、AI和整场战斗能从MySQL恢复。

**Outbox是不是恰好一次？** 不是。业务事务写Outbox，Worker提交published后尝试推送，之间仍可失败。客户端查结果／活动恢复；防重裁决在业务层，不将网络投递误写成恰好一次。

**旧Worker结果怎么不覆盖新安排？** 更新匹配所选状态、attempts和next_attempt_at；任务处理中被重排或已完成时，旧结果更新不到新状态。真实MySQL回归验证DATETIME(3)和条件更新，不是并发压测。

**做过哪些验证？** 当前73组Unity原生、23组兼容、普通构建与双角色UI；Go单元／vet／模块校验，当前v8隔离MySQL／Redis6个顶层加9个Worker子例，启用race、零失败／跳过。子例、模型、真人反馈分开计算，见[状态](current-status.md)。

**能承受多少玩家／QPS？** 最多4人是Demo设计边界，没有商业并发结论。真人反馈缺完整人数／设备记录，不说四人跨机稳定通过；取消的重测不拿旧指标补。

**以后开放公网要做什么？** 重新评估传输保护、网关、身份与滥用防护、不可信主机、运维与故障恢复；私有IP允许列表与Radmin不替代公网设计，见[威胁模型](game-realtime-gm-demo-threat-model.md)。

## 简历表述草稿

按自己能解释的工作选2—3条，结合[项目摘要](resume-project-summary.md)调整：

- 实现Go/Gin/WebSocket业务层，覆盖身份、组队匹配、版本化准入、Run控制、合同及持久化结算，配合独立Unity权威提供本机／受限LAN合作试玩。
- 设计Run／来源／连接代次、操作与事实指纹及有界乱序回执，通过事务和奖励唯一约束处理重连、重复交付与结算重试，保留可查询结果与账本。
- 补齐隔离MySQL／Redis集成、Worker重试／陈旧结果回归及构建／源码包指纹审计，明确自动检查、用户试玩、历史结果与未验能力边界。

公开后使用实际仓库地址；徽章和数字只对应真实提交结果。无依据的性能百分比、并发量、零故障时长和生产SLO不进简历。
