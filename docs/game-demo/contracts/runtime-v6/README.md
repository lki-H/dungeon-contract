# v6 实际线格式与计划逻辑字段的映射

运行候选dc_ruins.v6.p5.2。planned-v6保留最初规划状态；这里记录真实codec，不将合成样例当真人或UDP证据。

- Go可靠事实与HTTP分别见上级frame-v6-p5.schema.json和openapi-v6-p5-runtime.json。
- 生命周期见lifecycle.schema.json；单事件≤2048 UTF8字节、每批≤64及16KiB，限长与因果/跨字段关系由代码另验。
- 每连接消息包络绑定Run/source generation/connection generation；事件中的room_id与interest_revision配合裁剪，seq在裁剪后分配。handle整Run不复用，所以实体代次隐式固定为1；玩家来源生命另用source_life。
- root为整次出手ID，projectile_N为单弹instance/handle；同一目标生命的命中由权威Hit集合去重，客户端不提交结论。
- bounce的state位置为真实接触、direction为新出向、trajectory_revision递增；上一段已知入向与新出向确定反射法线，客户端只展示，不裁决碰撞。清弹真实归属走已有actor/cast反馈与有限Go保护证明，不把原射手误记为清弹者。
- 动态头：protocol u8,source i64,connection i64,state revision i32,group u8,world frame i64,game ms i64,count u8,interest u32,lifecycle watermark u32，共47字节。
- 弹体32字节：handle u32,x/y i32毫格,dir x/y i16千分比,speed/radius u16毫单位,flags/bounce/pierce/bucket各u8,trajectory revision u32,born delivery u32。
- 非弹体32字节：handle u32,x/y i32,input ack u32,dash remaining u16,dash dir x/y i16,dash speed u16毫格每秒,health/max i32。43—51组，实体目录由可靠基线固定；未用组52—63保留。
- 完整基线使用Base64二进制弹体行、root/owner字典和冻结伤害/连锁/冰霜目录；总UTF8仍≤256KiB。静态位置来自随包世界，当前受观察房的动态箱血/陷阱和角色沿原字段传输；不把全19变体内联入局消息。
- ACK与resync均绑定上述代次及interest；ACK100ms目标/60每秒上限，resync至多每秒一次。恢复与重复事件的语义由DcV6Sync/DcV6Runtime及专项回归验证。

生命周期样例lifecycle.example.json来自实际Unity权威编码器的隔离模型（v6_model），是格式验证素材，未运行真实Run；不含有效票据或凭据。
