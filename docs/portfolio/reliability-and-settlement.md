# 可靠事实、重试与奖励结算

项目的可靠性目标是：断线、乱序和重复请求不能直接变成重复合同进度或重复奖励。实现依靠不同层次的身份、事务和恢复水位；网络投递与业务生效分开处理。

## 不同身份解决不同问题

| 身份／约束 | 对应问题 | 实现 |
| --- | --- | --- |
| 玩家+operation_id+action+请求指纹 | 丢响应后重提；同ID换内容 | [store.Command](../../backend/internal/pve/store/store.go)保存结果，同指纹返回结果、冲突拒绝 |
| Run+assignment+boot+source_generation | 旧权威或旧分配继续提交 | [Service](../../backend/internal/pve/gameplay/service.go)、[Frame](../../backend/internal/pve/gameplay/frame.go)校验当前分配与来源 |
| 玩家连接代次与session | 旧连接覆盖已重连玩家 | [Issue／Consume](../../backend/internal/pve/gameplay/service.go)绑定当前会话与递增连接代次 |
| frame_id+sequence+内容指纹 | 乱序或重复事实帧 | [frame.go](../../backend/internal/pve/gameplay/frame.go)保存received／applied回执，只连续推进水位 |
| 事件、根动作与伤害实例 | 多箭／多跳伤害与合同动作计数不同 | [运行契约](../game-demo/contracts/runtime-v8/README.md)及[事实处理](../../backend/internal/pve/gameplay/frame.go) |
| Run+玩家+reward_source唯一性 | 重试发出第二份相同奖励 | [settlement.grant](../../backend/internal/pve/settlement/service.go)与数据库唯一约束 |

不能仅凭生成UUID认定幂等。需要用身份查已有结果、校验内容、拒绝冲突，并把结果与业务修改放在同一事务中。

## 事实确认有两个水位

Go先校验权威事实帧的来源、内容版本、控制栅栏、字节／数量和重排窗口，再持久化。后续序号先到时可处于received；只有从已应用序号之后连续的帧才变为applied并推进合同或终态。

同帧重发且指纹相同返回保存的回执，内容变更返回冲突。待处理数量、字节与序号窗口受限，避免无限缓存。缺口超时与技术等待有显式状态，不把未确认事实写成最终成绩。见[事实时序](architecture-overview.md#2-权威事实乱序与重试)。

## 结算事务与唯一奖励

Worker调用 `Settle`，对每个参与者执行独立事务：锁定Run，检查已有settled结果，计算合法奖励，锁定玩家资产行，检查奖励来源，再写奖励记录、余额、账本、参与者结果与Outbox。这些修改一起提交或回滚；0奖励不会制造虚假入账。

`FinalizeRun`等待所有成员结果完成后关闭Run并恢复队伍可用状态。部分参与者先成功不要求撤销已完成的事务；重试跳过已有结果，补齐剩余成员。代码：[SettleParticipant／FinalizeRun／grant](../../backend/internal/pve/settlement/service.go)。表与约束见[基础迁移](../../backend/internal/database/migrations/day37_v2_pve_foundation.sql)和[账本迁移](../../backend/internal/database/migrations/day31_settlement_assets.sql)。

## 旧Worker结果不能覆盖新安排

Worker只分派settlement与task_completion两类已知操作。缺少处理器或未知类型进入needs_repair；可重试故障按有限次数退避。结果更新同时匹配操作ID、允许状态、所选attempts和next_attempt_at，避免处理中出现的新计划或已完成状态被旧回调覆盖。

[真实MySQL回归](../../backend/internal/pve/gameplay/v8_worker_integration_test.go)覆盖9个子例，包括分派、隔离、DATETIME(3)、陈旧结果与失败后重试。[11月10日记录](review-20261110.md)的race检查包含这些子例；它验证真实条件更新，不表示做过双Worker并发压测。

## 通知和投影可以恢复

业务事务内的 `store.Notify`写Outbox。Worker有界扫描待发布记录，畸形记录隔离；合法记录先更新published并提交，再尝试发送。进程可能在提交后、发送前失败，所以客户端必须能查询MySQL里的结果。没有客户端推送恰好接收一次的承诺。

Redis保存匹配队列与奖励投影，重建从MySQL读取，不裁决奖励唯一性。可重建缓存也不意味着能恢复整场实时战斗。

代码：[Outbox写入](../../backend/internal/pve/store/store.go)、[通知处理](../../backend/internal/pve/worker/worker.go)、[投影重建](../../backend/internal/pve/projection/service.go)。

## 当前验证范围

Go业务仍为同库单实例，Unity是战斗事实信任根。后端测试、当前v8真实存储／race、Unity模型、实机UI和用户联机反馈分别记录，不互相替代，也不推导商业并发、跨区域容灾或公网抗攻击能力。执行命令与隔离要求见[测试说明](testing-and-evidence.md)。
