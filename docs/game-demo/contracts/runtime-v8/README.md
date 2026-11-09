# 战斗协议8补充

[当前状态与范围](../../../portfolio/current-status.md)、[协议与数据职责](../../../portfolio/protocol-and-data.md)。combat-state.schema.json校验新增玩家字段，原主体、装备、生命和连接字段继续由既有基线交接。真实伤害和状态只由Unity权威产生；客户端不提交次数、期限、伤害、落点或形状。

部署请求仍为ability_primary，F仍为ability_secondary，继承Run/来源代次/连接代次/输入序号/武器修订和操作幂等。第一次成功扣1次，第二次成功才写冷却，失败不扣；恢复保留组ID、余次及绝对期限。每台塔额外传deployment_group、born_game_ms、expires_game_ms、owner_life。

锤风冻结伤害、冰霜、连锁，每跳有独立接触ID且共享根；原目标生命/同帧地形与合同过滤保持。暂停冻结游戏钟；技术等待不补历史跳数；恢复不重放旧落地表现。新旧协议、内容、世界、参数指纹严格隔离，WS仍V2，无SQL迁移。

角色增量消息不单独套用完整combat-state Schema；先依据Run/来源代次/连接代次及state_revision合并到当前可靠基线，再校验完整角色状态。增量只省略未变属性，保留玩家标识/weapon_revision及原收包必需数组；新修订发送完整必要属性。弹体生命周期与事实不采用这种属性合并。详见[角色状态Schema](combat-state.schema.json)。
