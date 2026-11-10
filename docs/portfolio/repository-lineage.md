# 项目前身与共享维护

Dungeon Contract从[game-realtime-gm](https://github.com/lki-H/game-realtime-gm)的 `401247d03832cb10775f166f2deeb71310acc150` 源码快照派生。旧仓库保留模块化V2／GM基础、历史实际回退和独立RPC／消息实验；本仓库承担Unity权威战斗、玩法和完整试玩交付。简历只计算一个演进项目，主入口为本仓库。

## 共享修复对应

| 问题 | Dungeon Contract | 前身仓库 |
| --- | --- | --- |
| JWT期限与密码字节边界 | 11月7日修复，11月9日与公开提交分别验证 | 2026-11维护分支定向回移，在原V2模型上重新验证 |
| Worker分派与陈旧尝试保护 | 11月10日真实MySQL／race子例 | 使用旧库自己的真实存储fixture，不复制v8验收结果 |
| Bearer格式 | 本库已有通用解析及回归 | 仅回移认证入口，不带战斗准入或试炼 |
| 前端未知结果与迟到回执 | 本库Control回归及浏览器夹具 | 保留原训练场V2业务，分别测试旧socket与原操作关联 |
| 安全本机启动 | 本库专用数据库与端口 | 新隔离环境18080／33306／36379；不能接管本库安装 |

各库实际提交与检查见各自PR、维护说明及GitHub Actions。相同问题的源代码可参考，但源码变化与测试成绩不互相替代；文档日期不是自动部署声明。旧仓库没有本库day41以后Schema、试炼、贡献卡或战斗协议，不能整包回移。

本轮关联交付为[旧库PR #6](https://github.com/lki-H/game-realtime-gm/pull/6)和[本库PR #1](https://github.com/lki-H/dungeon-contract/pull/1)。本库运行源码基线为`7f76d141e1a80803a923705366355aa9f0019507`；旧库认证／Worker／Control对应`d262302`，隔离入口对应`feef71a`及`02bf794`，独立R5对应`eca8e8c`。逐项范围、后继修复与实际Actions见[旧库维护记录](https://github.com/lki-H/game-realtime-gm/blob/main/docs/maintenance/README.md)。旧库这轮额外增加了“缺少code的通知不能结束pending请求”的防御；本库这次仅同步文档，不宣称已经合入该额外分支或复制其测试成绩。

R5只在前身仓库维护：RPC查询隔离恢复副本，MQ消费者写独立报表；主业务不依赖它发奖。本库未因此获得微服务、消息总线、跨进程战斗恢复或商业容量能力。

本次关联更新仅改文档，不改变已验证二进制、SQL、运行配置和用户已取消的性能／整局复验范围。
