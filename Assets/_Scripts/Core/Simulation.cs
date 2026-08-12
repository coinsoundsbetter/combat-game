using System;

namespace FightGame
{
    // 确定性模拟：纯函数，不调用任何 Unity API。
    // 相同 (state, inputs) 必须得到相同结果 —— 这是回滚与回放共同的根基。
    public static class Simulation
    {
        public const int STAGE_HALF = 300;   // 舞台半宽 cm
        public const int WALK_SPEED = 5;     // cm/帧
        public const int JUMP_VEL = 18;      // cm/帧
        public const int GRAVITY = 1;        // cm/帧^2
        public const int MAX_HEALTH = 100;

        public static GameState CreateInitialState(uint seed)
        {
            var s = new GameState { frame = -1, rng = seed, roundTimer = 60 * 99 };
            s.fighters[0] = new FighterState { health = MAX_HEALTH, x = -100, y = 0, move = MoveId.Idle, facingRight = true,  onGround = true };
            s.fighters[1] = new FighterState { health = MAX_HEALTH, x =  100, y = 0, move = MoveId.Idle, facingRight = false, onGround = true };
            return s;
        }

        public static MoveDef GetMove(MoveId id)
        {
            switch (id)
            {
                case MoveId.Punch:
                    return new MoveDef { id = id, startup = 3, active = 3, recovery = 7,
                        damage = 10, hitstun = 18, blockstun = 8, knockbackX = 6, knockbackY = 0, hitstop = 4 };
                default:
                    return new MoveDef { id = id, startup = 0, active = 0, recovery = 1,
                        damage = 0, hitstun = 0, blockstun = 0, knockbackX = 0, knockbackY = 0, hitstop = 0 };
            }
        }

        // 推进一帧。state 会被原地改写；调用方负责快照保存。
        public static void AdvanceFrame(GameState s, InputState in0, InputState in1)
        {
            s.frame++;
            s.roundTimer = Math.Max(0, s.roundTimer - 1);

            // 全局 hitstop：命中后双方冻结几帧制造打击感，但帧计数器照常走。
            int hs = Math.Max(s.fighters[0].hitstop, s.fighters[1].hitstop);
            if (hs > 0)
            {
                s.fighters[0].hitstop--; s.fighters[1].hitstop--;
                return;
            }

            var inputs = new[] { in0, in1 };
            for (int i = 0; i < 2; i++)
            {
                var f = s.fighters[i];
                var opp = s.fighters[1 - i];
                var input = inputs[i];
                f.facingRight = f.x <= opp.x;

                // 受击硬直：不能行动，只滑退 + 衰减
                if (f.hitstun > 0)
                {
                    f.hitstun--;
                    f.vx -= f.vx / 4; // 摩擦衰减击退
                    if (f.hitstun == 0) { f.move = MoveId.Idle; f.moveFrame = 0; f.vx = 0; }
                    ApplyPhysics(ref f);
                    s.fighters[i] = f;
                    continue;
                }
                if (f.blockstun > 0)
                {
                    f.blockstun--;
                    f.vx -= f.vx / 4;
                    if (f.blockstun == 0) { f.move = MoveId.Idle; f.moveFrame = 0; f.vx = 0; }
                    ApplyPhysics(ref f);
                    s.fighters[i] = f;
                    continue;
                }

                bool canAct = f.move == MoveId.Idle || f.move == MoveId.Walk ||
                              f.move == MoveId.Jump || f.move == MoveId.Block;

                if (canAct && input.Punch)
                {
                    f.move = MoveId.Punch; f.moveFrame = 0; f.hasHitThisMove = false; f.vx = 0;
                }
                else if (canAct && input.Block)
                {
                    f.move = MoveId.Block; f.moveFrame = 0; f.vx = 0;
                }
                else if (canAct)
                {
                    int dir = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
                    if (f.onGround)
                    {
                        f.vx = dir * WALK_SPEED;
                        if (input.Up) { f.vy = JUMP_VEL; f.onGround = false; f.move = MoveId.Jump; f.moveFrame = 0; }
                        else if (dir != 0) { if (f.move != MoveId.Walk) { f.move = MoveId.Walk; f.moveFrame = 0; } }
                        else { if (f.move != MoveId.Idle) { f.move = MoveId.Idle; f.moveFrame = 0; } f.vx = 0; }
                    }
                }

                f.moveFrame++;
                if (f.move == MoveId.Punch && f.moveFrame >= GetMove(MoveId.Punch).Total)
                { f.move = MoveId.Idle; f.moveFrame = 0; }
                else if (f.move == MoveId.Block && f.moveFrame >= 1)
                { f.moveFrame = 0; } // Block 靠输入维持，每帧重判

                ApplyPhysics(ref f);
                s.fighters[i] = f;
            }

            ResolveHits(s);
        }

        static void ApplyPhysics(ref FighterState f)
        {
            if (!f.onGround)
            {
                f.vy -= GRAVITY;
                f.y += f.vy;
                if (f.y <= 0) { f.y = 0; f.vy = 0; f.onGround = true; if (f.move == MoveId.Jump) { f.move = MoveId.Idle; f.moveFrame = 0; } }
            }
            f.x += f.vx;
            if (f.x < -STAGE_HALF) f.x = -STAGE_HALF;
            if (f.x >  STAGE_HALF) f.x =  STAGE_HALF;
        }

        struct Box { public int x0, y0, x1, y1; }

        static Box Hurtbox(in FighterState f) =>
            new Box { x0 = f.x - 30, x1 = f.x + 30, y0 = f.y, y1 = f.y + 120 };

        static Box PunchHitbox(in FighterState f)
        {
            int a = f.facingRight ? f.x + 10 : f.x - 70;
            int b = f.facingRight ? f.x + 70 : f.x - 10;
            return new Box { x0 = Math.Min(a, b), x1 = Math.Max(a, b), y0 = f.y + 40, y1 = f.y + 100 };
        }

        static bool Intersect(in Box a, in Box b) =>
            !(a.x1 < b.x0 || b.x1 < a.x0 || a.y1 < b.y0 || b.y1 < a.y0);

        // 伤害检测：整数 AABB 相交，确定性。
        static void ResolveHits(GameState s)
        {
            for (int atk = 0; atk < 2; atk++)
            {
                int def = 1 - atk;
                var a = s.fighters[atk];
                var d = s.fighters[def];
                if (a.move != MoveId.Punch || a.hasHitThisMove) continue;

                var md = GetMove(MoveId.Punch);
                if (a.moveFrame < md.startup || a.moveFrame >= md.startup + md.active) continue;

                if (!Intersect(PunchHitbox(a), Hurtbox(d))) continue;

                a.hasHitThisMove = true;
                bool blocking = d.move == MoveId.Block;
                if (blocking)
                {
                    d.blockstun = md.blockstun;
                    d.vx = (a.facingRight ? 1 : -1) * (md.knockbackX / 2);
                    d.move = MoveId.Block; d.moveFrame = 0;
                }
                else
                {
                    d.health = Math.Max(0, d.health - md.damage);
                    d.hitstun = md.hitstun;
                    d.vx = (a.facingRight ? 1 : -1) * md.knockbackX;
                    d.vy = md.knockbackY;
                    if (d.vy != 0) d.onGround = false;
                    d.move = MoveId.Hit; d.moveFrame = 0;
                }
                a.hitstop = md.hitstop; d.hitstop = md.hitstop;
                s.fighters[atk] = a;
                s.fighters[def] = d;
            }
        }
    }
}
