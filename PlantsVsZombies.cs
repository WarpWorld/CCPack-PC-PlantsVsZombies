using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using ConnectorLib;
using CrowdControl.Common;
using Log = CrowdControl.Common.Log;
using ConnectorLib.Inject.AddressChaining;
using ConnectorLib.Inject.Payload.DirectX;
using ConnectorLib.Inject.VersionProfiles;
using ConnectorType = CrowdControl.Common.ConnectorType;

namespace CrowdControl.Games.Packs
{
    public class PlantsVsZombies : InjectEffectPack
    {
        public override Game Game { get; } = new(144, "Plants vs Zombies", "PlantsVsZombies", "PC", ConnectorType.PCConnector);

        #region AddressChains

        //Base G - Game Data
        private AddressChain game_ptr_ch;
        private AddressChain game_ch;
        private AddressChain collision_ch;
        private AddressChain collect_ch;
        private AddressChain slow_bullets_ch;
        private AddressChain invincible_zombies_ch;
        private AddressChain high_gravity_bullets_ch;
        private AddressChain backwards_bullets_ch;
        private AddressChain freeze_bullets_ch;
        private AddressChain invincible_plants_ch1;
        private AddressChain invincible_plants_ch2;
        private AddressChain one_hit_kill_ch1;
        private AddressChain one_hit_kill_ch2;
        private AddressChain one_hit_kill_ch3;
        private AddressChain zombies_speed_ch;


        private const byte JZ = 0x84;
        private const byte JNZ = 0x85;
        private const byte JNZ_SHORT = 0x75;
        private const byte JMP = 0xEB;
        private const byte NOP = 0x90;
        private const ushort NOP_NOP = 0x9090;
        private byte[] NOP_NOP_NOP = { 0x90, 0x90, 0x90 };
        private const int NOP_NOP_NOP_NOP = -1869574000;

        private const int MIN_PERCENTAGE = 10;
        private const int MAX_PERCENTAGE = 50;
        private const int MIN_BIG_PERCENTAGE = 40;
        private const int MAX_BIG_PERCENTAGE = 70;

        private const int EFFECT_DURATION = 15;

        private const int MAX_SUN = 9999;

        private const int MAX_USABLE_CARD = 39;

        private const int ZOMBIE_OBJECT_SIZE = 0x168;
        private const int PLANT_OBJECT_SIZE = 0x14C;

        private const float SIZE_SMALL = 0.5f;
        private const float SIZE_NORMAL = 1.0f;
        private const float SIZE_BIG = 2.0f;

        private const int MIN_VISIBLE_X = 700;

        private long imagebase;

        private List<int> original_max_cooldowns = new();
        private List<int> new_max_cooldowns = new();
        private List<int> cards = new();

        byte[] free_space_size_addr;

        #endregion

        private enum CARD_STATUS
        {
            SELECTED = 0,
            READY = 1,
            RECHARGING = 256
        }

        private enum PLANT
        {
            DISABLED = -1,
            PEA = 0,
            SUN,
            CHERRY,
            WALLNUT,
            MINE,
            ICE_PEA
        }

        private enum LANE
        {
            TOP = 0,
            SECOND = 1,
            CENTER = 2,
            FOURTH = 3,
            BOTTOM = 4
        }

        public PlantsVsZombies(IPlayer player, Func<CrowdControlBlock, bool> responseHandler, Action<object> statusUpdateHandler)
            : base(player, responseHandler, statusUpdateHandler)
        {
            VersionProfiles = new List<VersionProfile>
                              {
                                  new("popcapgame1", InitGame, DeinitGame, null, Direct3DVersion.Direct3D9)
                              };
        }

        #region [De]init

        private void InitGame()
        {
            var imagebase_ch = AddressChain.AOB(this, 0, Encoding.ASCII.GetBytes("This program cannot be run in DOS mode"), "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", -(0x4E), ScanHint.None);
            imagebase_ch.Calculate(out imagebase);

            // Top class

            byte[] top_class_pattern = { 0x8B, 0x0D, 0xAF, 0xAF, 0xAF, 0xAF, 0x8B, 0x89, 0xAF, 0xAF, 0xAF, 0xAF, 0x83, 0xF9, 0x14 };
            var tmp_ch = AddressChain.AOB(this, 0, top_class_pattern, "xx????xx????xxx", 2, ScanHint.ExecutePage, imagebase);

            game_ptr_ch = tmp_ch.Follow().Follow().Offset(0x868);

            // Collision

            byte[] collision_pattern = { 0xF, 0x85, 0xA9, 0x4, 0x0, 0x0 };
            collision_ch = AddressChain.AOB(this, 0, collision_pattern, "xxxxxx", 1, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Collect

            byte[] collect_pattern = { 0x75, 0xAF, 0x8B, 0xAF, 0xE8, 0xAF, 0xAF, 0xAF, 0xAF, 0xEB, 0xAF, 0x8B, 0xAF, 0xE8, 0xAF, 0xAF, 0xAF, 0xAF, 0x83 };
            collect_ch = AddressChain.AOB(this, 0, collect_pattern, "x?x?x????x?x?x????x", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Slow bullets

            byte[] slow_bullets_pattern = { 0x75, 0x75, 0xD9, 0xEE, 0xD8, 0x55, 0x44 };
            slow_bullets_ch = AddressChain.AOB(this, 0, slow_bullets_pattern, "xxxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Invincible zombies

            byte[] invincible_zombies_pattern = { 0xF, 0x85, 0x9B, 0x00, 0x00, 0x00, 0x8B, 0x8D };
            invincible_zombies_ch = AddressChain.AOB(this, 0, invincible_zombies_pattern, "xxxxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // High gravity bullets

            byte[] high_gravity_bullets_pattern = { 0x75, 0x23, 0x83, 0x7D, 0x58, 0x08 };
            high_gravity_bullets_ch = AddressChain.AOB(this, 0, high_gravity_bullets_pattern, "xxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Backwards bullets

            byte[] backwards_bullets_pattern = { 0x75, 0x20, 0x83, 0x7D, 0x60, 0x3C };
            backwards_bullets_ch = AddressChain.AOB(this, 0, backwards_bullets_pattern, "xxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Freeze bullets

            byte[] freeze_bullets_pattern = { 0x75, 0x07, 0xE8, 0xB9, 0xF7, 0xFF, 0xFF };
            freeze_bullets_ch = AddressChain.AOB(this, 0, freeze_bullets_pattern, "xxxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Invincible plants

            byte[] invincible_plants_pattern = { 0x29, 0x50, 0x40, 0x83, 0xF9, 0x19 };
            invincible_plants_ch1 = AddressChain.AOB(this, 0, invincible_plants_pattern, "xxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            byte[] invincible_plants_pattern2 = { 0x83, 0x46, 0x40, 0xFC, 0x8B, 0x4E, 0x40 };
            invincible_plants_ch2 = AddressChain.AOB(this, 0, invincible_plants_pattern2, "xxxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // One hit kill

            byte[] one_hit_kill_pattern = { 0x8B, 0xAF, 0xC8, 0x00, 0x00, 0x00 };
            one_hit_kill_ch1 = AddressChain.AOB(this, 0, one_hit_kill_pattern, "xxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            byte[] one_hit_kill_pattern2 = { 0x8B, 0x8D, 0xD0, 0x00, 0x00, 0x00, 0xB8 };
            one_hit_kill_ch2 = AddressChain.AOB(this, 0, one_hit_kill_pattern2, "xxxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            byte[] one_hit_kill_pattern3 = { 0x29, 0x86, 0xDC, 0x00, 0x00, 0x00 };
            one_hit_kill_ch3 = AddressChain.AOB(this, 0, one_hit_kill_pattern3, "xxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();

            // Zombies speed

            byte[] zombies_speed_pattern = { 0xD8, 0x4B, 0x08, 0x5B, 0xD9, 0x5C, 0x24, 0x04 };
            zombies_speed_ch = AddressChain.AOB(this, 0, zombies_speed_pattern, "xxxxxxxx", 0, ScanHint.ExecutePage, imagebase).Cache().PreCache();
        }

        private void DeinitGame()
        {
        }

        #endregion

        #region Effect List
        public override List<Effect> Effects
        {
            get
            {
                List<Effect> result = new List<Effect>
                {
                    new("Infinite Sun", "infinitesun"),
                    new("Give Sun", "sun_up", new []{"quantity9999"}),
                    new("Take Sun", "sun_down", new []{"quantity9999"}),
                    new("No Cooldown", "nocooldown"),
                    new("Increase Cooldown", "cooldown_up"),
                    new("Decrease Cooldown", "cooldown_down"),
                    new("Can't plant", "cantplant"),
                    new("Plant Anywhere", "plantanywhere"),
                    new("Auto Collect", "autocollect"),
                    new("Invincible Zombies", "invinciblezombies"),
                    new("Slow Bullets", "slowbullets"),
                    new("Fast Bullets", "fastbullets"),
                    new("High Gravity Bullets", "highgravitybullets"),
                    new("Backwards Bullets", "backwardsbullets"),
                    new("Freeze Bullets", "freezebullets"),
                    new("Invincible Plants", "invincibleplants"),
                    new("One Hit Kill", "onehitkill"),
                    new("Big Zombies", "zombiessize_big"),
                    new("Small Zombies", "zombiessize_small"),
                    new("Random Cards", "randomcards"),
                    new("Fast Zombies", "zombiesspeed_faster"),
                    new("Slow Zombies", "zombiesspeed_slower"),
                    new("Zombies in the middle", "zombiesmiddle"),
                    new("Invisible Zombies", "invisiblezombies"),
                    new("Teleport Zombies to House", "teleportzombies"),
                    new("Charm Zombies", "charmzombies"),
                    new("Clear Zombies", "clearzombies"),
                    new("Clear Plants", "clearplants"),
                    new("Shuffle Cards", "shufflecards"),
                };

                return result;
            }
        }

        public override List<ItemType> ItemTypes { get; } = new()
        {
            new ItemType("Quantity", "quantity9999", ItemType.Subtype.Slider, "{\"min\":1,\"max\":9999}")
        };

        #endregion

        protected override bool IsReady(EffectRequest request)
        {
            bool res = false;
            if (game_ptr_ch.GetInt() != 0)
            {
                game_ch = game_ptr_ch.Follow();
                res = true;
            }
            return res;
        }

        protected override void StartEffect(EffectRequest request)
        {
            if (!IsReady(request))
            {
                return;
            }

            if (!is_not_paused())
            {
                DelayEffect(request);
                return;
            }

            var code = request.FinalCode.Split('_');
            switch (code[0])
            {
                case "infinitesun":
                    {
                        AddressChain sun_ch = game_ch.Offset(0x5578);
                        int old_sun = sun_ch.GetInt();
                        var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                        () => true,
                        () => Connector.SendMessage($"{request.DisplayViewer} gave you infinite sun !"), TimeSpan.FromSeconds(1),
                        () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            game_ch.Offset(0x5578).SetInt(MAX_SUN);
                            return true;

                        }, TimeSpan.FromMilliseconds(500), false, "sun");
                        act.WhenCompleted.Then(t =>
                        {
                            sun_ch.SetInt(old_sun);
                            Connector.SendMessage("Infinite sun ended !");
                        });
                        break;
                    }
                case "sun":
                    {
                        if (!int.TryParse(code[2], out int quantity))
                        {
                            Respond(request, EffectStatus.FailTemporary, "Invalid quantity");
                            break;
                        }

                        bool give = !string.Equals(code[1], "down");
                        if (!give) quantity *= -1;

                        AddressChain sun_ch = game_ch.Offset(0x5578);
                        int sun = sun_ch.GetInt();
                        if ((give && sun == MAX_SUN) || (!give && sun == 0)) { DelayEffect(request); return; }

                        TryEffect(request,
                            () => true,
                            () =>
                            {
                                int new_sun = sun + quantity;
                                if (new_sun > MAX_SUN) { new_sun = MAX_SUN; }
                                else if (new_sun < 0) { new_sun = 0; }
                                sun_ch.SetInt(new_sun);
                                return true;
                            },
                            () => Connector.SendMessage($"{request.DisplayViewer} {(give ? "sent" : "took")} {Math.Abs(quantity)} units of sun."),
                            null, true, "sun");
                        break;
                    }
                case "nocooldown":
                    {
                        AddressChain cards_ptr_ch = game_ch.Offset(0x15C);
                        if (cards_ptr_ch.GetInt() != 0)
                        {
                            AddressChain cards_ch = cards_ptr_ch.Follow();
                            int ncards = cards_ch.Offset(0x24).GetInt();

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} gave you no cooldown !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x4C + i * 0x50).SetInt(cards_ch.Offset(0x4C + i * 0x50 + 4).GetInt()); // cooldown = maxcooldown
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "cooldown");
                            act.WhenCompleted.Then(t =>
                            {
                                Connector.SendMessage("No cooldown ended !");
                            });
                        }
                        break;
                    }
                case "cooldown":
                    {
                        AddressChain cards_ptr_ch = game_ch.Offset(0x15C);
                        if (cards_ptr_ch.GetInt() != 0 && !original_max_cooldowns.Any() && !new_max_cooldowns.Any())
                        {
                            AddressChain cards_ch = cards_ptr_ch.Follow();
                            int ncards = cards_ch.Offset(0x24).GetInt();

                            bool is_up = string.Equals(code[1], "up");

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => 
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    int max_cooldown = cards_ch.Offset(0x50 + i * 0x50).GetInt();
                                    original_max_cooldowns.Add(max_cooldown);
                                    int new_cooldown;
                                    if (is_up) { new_cooldown = max_cooldown + random_big_percentage(max_cooldown); } else { new_cooldown = max_cooldown - random_big_percentage(max_cooldown); }
                                    new_max_cooldowns.Add(new_cooldown);
                                }
                                Connector.SendMessage($"{request.DisplayViewer} increased your cooldown !");
                                return true;
                            }
                            , TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x50 + i * 0x50).SetInt(new_max_cooldowns[i]);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "cooldown");
                            act.WhenCompleted.Then(t =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x50 + i * 0x50).SetInt(original_max_cooldowns[i]);
                                }
                                original_max_cooldowns.Clear();
                                new_max_cooldowns.Clear();
                                Connector.SendMessage("Cooldown back to normal !");
                            });
                        }
                        break;
                    }
                case "cantplant":
                    {
                        AddressChain cards_ptr_ch = game_ch.Offset(0x15C);
                        if (cards_ptr_ch.GetInt() != 0)
                        {
                            AddressChain cards_ch = cards_ptr_ch.Follow();
                            int ncards = cards_ch.Offset(0x24).GetInt();

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} made you unable to plant !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x70 + i * 0x50).SetInt((int)CARD_STATUS.SELECTED);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "cooldown");
                            act.WhenCompleted.Then(t =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x70 + i * 0x50).SetInt((int)CARD_STATUS.READY);
                                }
                                Connector.SendMessage("Can't plant ended !");
                            });
                        }
                        break;
                    }
                case "plantanywhere":
                    {
                        if (collision_ch.GetByte() == JZ) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            collision_ch.SetByte(JZ);
                            Connector.SendMessage($"{request.DisplayViewer} allowed you to plant anywhere !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "collision");

                        tim.WhenCompleted.Then(t =>
                        {
                            collision_ch.SetByte(JNZ);
                            Connector.SendMessage("Planting back to normal !");
                        });
                        break;
                    }
                case "autocollect":
                    {
                        if (collect_ch.GetByte() == JMP) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            collect_ch.SetByte(JMP);
                            Connector.SendMessage($"{request.DisplayViewer} gave you auto collect !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "collect");

                        tim.WhenCompleted.Then(t =>
                        {
                            collect_ch.SetByte(JNZ_SHORT);
                            Connector.SendMessage("Auto collect ended !");
                        });
                        break;
                    }
                case "invinciblezombies":
                    {
                        if (invincible_zombies_ch.GetByte() == NOP) { DelayEffect(request); return; }

                        if (!is_zombie_out()) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            byte[] nops = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
                            invincible_zombies_ch.SetBytes(nops);
                            Connector.SendMessage($"{request.DisplayViewer} made zombies invincible !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "zombieshealth");

                        tim.WhenCompleted.Then(t =>
                        {
                            byte[] code = { 0xF, 0x85, 0x9B, 0x00, 0x00, 0x00 };
                            invincible_zombies_ch.SetBytes(code);
                            Connector.SendMessage("Invincible zombies ended !");
                        });
                        break;
                    }
                case "slowbullets":
                    {
                        if (GetUShort(slow_bullets_ch) == NOP_NOP) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            SetUShort(slow_bullets_ch, NOP_NOP);
                            Connector.SendMessage($"{request.DisplayViewer} made bullets slow !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "bullets");

                        tim.WhenCompleted.Then(t =>
                        {
                            SetUShort(slow_bullets_ch, 0x7575);
                            Connector.SendMessage("Slow bullets ended !");
                        });
                        break;
                    }
                case "highgravitybullets":
                    {
                        if (high_gravity_bullets_ch.GetByte() == NOP) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            SetUShort(high_gravity_bullets_ch, NOP_NOP);
                            Connector.SendMessage($"{request.DisplayViewer} increased the gravity on bullets !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "bullets");

                        tim.WhenCompleted.Then(t =>
                        {
                            SetUShort(high_gravity_bullets_ch, 0x2375);
                            Connector.SendMessage("Bullets gravity returned to normal !");
                        });
                        break;
                    }
                case "backwardsbullets":
                    {
                        if (backwards_bullets_ch.GetByte() == NOP) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            SetUShort(backwards_bullets_ch, NOP_NOP);
                            Connector.SendMessage($"{request.DisplayViewer} made plants to shoot backwards !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "bullets");

                        tim.WhenCompleted.Then(t =>
                        {
                            SetUShort(backwards_bullets_ch, 0x2075);
                            Connector.SendMessage("Plants direction of shooting back to normal !");
                        });
                        break;
                    }
                case "freezebullets":
                    {
                        if (freeze_bullets_ch.GetByte() == NOP) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            SetUShort(freeze_bullets_ch, NOP_NOP);
                            Connector.SendMessage($"{request.DisplayViewer} froze the bullets !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "bullets");

                        tim.WhenCompleted.Then(t =>
                        {
                            SetUShort(freeze_bullets_ch, 0x775);
                            Connector.SendMessage("Bullets are not frozen anymore !");
                        });
                        break;
                    }
                case "invincibleplants":
                    {
                        if (invincible_plants_ch1.GetByte() == NOP) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            invincible_plants_ch1.SetBytes(NOP_NOP_NOP);
                            invincible_plants_ch2.SetInt(NOP_NOP_NOP_NOP);
                            Connector.SendMessage($"{request.DisplayViewer} made plants invincible !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "plantshealth");

                        tim.WhenCompleted.Then(t =>
                        {
                            byte[] t1 = { 0x29, 0x50, 0x40 };
                            byte[] t2 = { 0x83, 0x46, 0x40, 0xFC };
                            invincible_plants_ch1.SetBytes(t1);
                            invincible_plants_ch2.SetBytes(t2);
                            Connector.SendMessage("Plants are not invincible anymore !");
                        });
                        break;
                    }
                case "onehitkill":
                    {
                        if (one_hit_kill_ch1.GetByte() == 0x33) { DelayEffect(request); return; }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            byte[] t1 = { 0x33, 0xED, 0x90, 0x90, 0x90, 0x90 }; // xor ebp, ebp
                            byte[] t2 = { 0x33, 0xC9, 0x90, 0x90, 0x90, 0x90 }; // xor ecx, ecx
                            byte[] t3 = { 0x33, 0xFF, 0x89, 0xBE, 0xDC, 0x00, 0x00, 0x00, 0x90, 0x90, 0x90, 0x90 }; // xor edi, edi | mov dword ptr ds:[esi+dc], edi
                            one_hit_kill_ch1.SetBytes(t1);
                            one_hit_kill_ch2.SetBytes(t2);
                            one_hit_kill_ch3.SetBytes(t3);
                            Connector.SendMessage($"{request.DisplayViewer} made every zombie to die in one hit !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "zombieshealth");

                        tim.WhenCompleted.Then(t =>
                        {
                            byte[] t1 = { 0x8B, 0xAF, 0xC8, 0x00, 0x00, 0x00 };
                            byte[] t2 = { 0x8B, 0x8D, 0xD0, 0x00, 0x00, 0x00 };
                            byte[] t3 = { 0x29, 0x86, 0xDC, 0x00, 0x00, 0x00, 0x8B, 0xBE, 0xDC, 0x00, 0x00, 0x00 };
                            one_hit_kill_ch1.SetBytes(t1);
                            one_hit_kill_ch2.SetBytes(t2);
                            one_hit_kill_ch3.SetBytes(t3);
                            Connector.SendMessage("One Hit Kill off !");
                        });
                        break;
                    }
                case "zombiessize":
                    {
                        AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
                        int nactive_zombies = game_ch.Offset(0xAC).GetInt();
                        if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
                        {
                            bool is_big = string.Equals(code[1], "big");

                            AddressChain active_zombies_ch = active_zombies_ptr.Follow();
                            AddressChain tmp_ch;

                            if (!is_one_zombie_in_visible_range(active_zombies_ch, nactive_zombies)) { DelayEffect(request); return ;  }

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} made all zombies " + (is_big ? "bigger" : "smaller") +  " !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0x11C).SetFloat(is_big ? SIZE_BIG : SIZE_SMALL);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "zombiessize");
                            act.WhenCompleted.Then(t =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0x11C).SetFloat(SIZE_NORMAL);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                Connector.SendMessage("Zombies size returned to normal !");
                            });
                        }
                        break;
                    }
                case "randomcards":
                    {
                        AddressChain cards_ptr_ch = game_ch.Offset(0x15C);
                        if (cards_ptr_ch.GetInt() != 0 && !cards.Any())
                        {
                            AddressChain cards_ch = cards_ptr_ch.Follow();
                            int ncards = cards_ch.Offset(0x24).GetInt();

                            var tim = StartTimed(request,
                            () => true,
                            () => is_not_paused(),
                            TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    AddressChain card_type = cards_ch.Offset(0x5C + i * 0x50);
                                    cards.Add(card_type.GetInt());
                                    card_type.SetInt(RNG.Next(0, MAX_USABLE_CARD));
                                }
                                Connector.SendMessage($"{request.DisplayViewer} changed all your cards !");
                                return true;
                            },
                            TimeSpan.FromSeconds(EFFECT_DURATION), "cards");

                            tim.WhenCompleted.Then(t =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x5C + i * 0x50).SetInt(cards[i]);
                                }
                                cards.Clear();
                                Connector.SendMessage("Cards returned to normal !");
                            });
                        }
                        break;
                    }
                case "zombiesspeed":
                    {
                        if (zombies_speed_ch.GetByte() == 0xC7) { DelayEffect(request); return; }

                        if (!is_zombie_out()) { DelayEffect(request); return; }

                        byte[] t1 = { 0xC7, 0x43, 0x08, 0x00, 0x00, 0x00, 0x00, 0xD8, 0x4B, 0x08, 0x5B, 0xD9, 0x5C, 0x24, 0x04, 0xD9, 0x44, 0x24, 0x04, 0x83, 0xC4, 0x14, 0xC3 };
                        switch (code[1])
                        {
                            case "faster": // 50.0
                                t1[5] = 0x48;
                                t1[6] = 0x42;
                                break;
                            case "slower": // 5.0
                                t1[5] = 0xA;
                                t1[6] = 0x40;
                                break;
                        }

                        var tim = StartTimed(request,
                        () => true,
                        () => is_not_paused(),
                        TimeSpan.FromMilliseconds(500),
                        () =>
                        {
                            zombies_speed_ch.SetBytes(t1);
                            Connector.SendMessage($"{request.DisplayViewer} made zombies" + code[1] + " !");
                            return true;
                        },
                        TimeSpan.FromSeconds(EFFECT_DURATION), "zombiesspeed");

                        tim.WhenCompleted.Then(t =>
                        {
                            byte[] t2 = { 0xD8, 0x4B, 0x08, 0x5B, 0xD9, 0x5C, 0x24, 0x04, 0xD9, 0x44, 0x24, 0x04, 0x83, 0xC4, 0x14, 0xC3 };
                            zombies_speed_ch.SetBytes(t2);
                            Connector.SendMessage("Zombies speed returned back to normal !");
                        });
                        break;
                    }
                case "zombiesmiddle":
                    {
                        AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
                        int nactive_zombies = game_ch.Offset(0xAC).GetInt();
                        if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
                        {
                            AddressChain active_zombies_ch = active_zombies_ptr.Follow();
                            AddressChain tmp_ch;

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} made all zombies to go to the middle !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0x1C).SetInt((int)LANE.CENTER);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "zombieslane");
                            act.WhenCompleted.Then(t =>
                            {
                                Connector.SendMessage("Zombies returned to their lanes !");
                            });
                        }
                        break;
                    }
                case "invisiblezombies":
                    {
                        AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
                        int nactive_zombies = game_ch.Offset(0xAC).GetInt();
                        if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
                        {
                            AddressChain active_zombies_ch = active_zombies_ptr.Follow();
                            AddressChain tmp_ch;

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} made all zombies invisible !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0x18).SetByte(0);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "zombiesinvisible");
                            act.WhenCompleted.Then(t =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0x18).SetByte(1);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                Connector.SendMessage("Zombies are visible again !");
                            });
                        }
                        break;
                    }
                case "teleportzombies":
                    {
                        AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
                        int nactive_zombies = game_ch.Offset(0xAC).GetInt();
                        if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
                        {
                            AddressChain active_zombies_ch = active_zombies_ptr.Follow();
                            AddressChain tmp_ch;

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} teleported all zombies to your house !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    AddressChain x_ch = tmp_ch.Offset(0x2C);
                                    if (x_ch.GetFloat() > 0.0f)
                                    {
                                        x_ch.SetFloat(0.0f);
                                    }
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "zombiesposition");
                            act.WhenCompleted.Then(t =>
                            {
                                Connector.SendMessage("Teleporting zombies ended !");
                            });
                        }
                        break;
                    }
                case "charmzombies":
                    {
                        AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
                        int nactive_zombies = game_ch.Offset(0xAC).GetInt();
                        if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
                        {
                            AddressChain active_zombies_ch = active_zombies_ptr.Follow();
                            AddressChain tmp_ch;

                            if (!is_one_zombie_in_visible_range(active_zombies_ch, nactive_zombies)) { DelayEffect(request); return; }

                            var act = RepeatAction(request, TimeSpan.FromSeconds(EFFECT_DURATION),
                            () => true,
                            () => Connector.SendMessage($"{request.DisplayViewer} charmed all the zombies !"), TimeSpan.FromSeconds(1),
                            () => is_not_paused(), TimeSpan.FromMilliseconds(500),
                            () =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0xB8).SetByte(1);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                return true;

                            }, TimeSpan.FromMilliseconds(500), false, "zombiesstatus");
                            act.WhenCompleted.Then(t =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0xB8).SetByte(0);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                Connector.SendMessage("Zombies stopped being charmed !");
                            });
                        }
                        break;
                    }
                case "clearzombies":
                    {
                        AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
                        int nactive_zombies = game_ch.Offset(0xAC).GetInt();
                        if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
                        {
                            AddressChain active_zombies_ch = active_zombies_ptr.Follow();
                            AddressChain tmp_ch;

                            if (!is_one_zombie_in_visible_range(active_zombies_ch, nactive_zombies)) { DelayEffect(request); return; }

                            TryEffect(request,
                            () => true,
                            () =>
                            {
                                tmp_ch = active_zombies_ch;
                                for (int i = 0; i < nactive_zombies; i++)
                                {
                                    tmp_ch.Offset(0x28).SetByte(1);
                                    tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
                                }
                                return true;
                            },
                            () => Connector.SendMessage($"{request.DisplayViewer} cleared all the zombies !"),
                            null, true, "zombieshealth");
                        }
                        break;
                    }
                case "clearplants":
                    {
                        AddressChain plants_ptr = game_ch.Offset(0xC4);
                        int nplants = game_ch.Offset(0xD4).GetInt();
                        if (plants_ptr.GetInt() != 0 && nplants > 0)
                        {
                            AddressChain plants_ch = plants_ptr.Follow();
                            AddressChain tmp_ch;

                            TryEffect(request,
                            () => true,
                            () =>
                            {
                                tmp_ch = plants_ch;
                                for (int i = 0; i < nplants;)
                                {
                                    AddressChain plant_is_dead_ch = tmp_ch.Offset(0x141);
                                    if (plant_is_dead_ch.GetByte() == 0)
                                    {
                                        plant_is_dead_ch.SetByte(1);
                                        i++;
                                    }
                                    tmp_ch = tmp_ch.Offset(PLANT_OBJECT_SIZE);
                                }
                                return true;
                            },
                            () => Connector.SendMessage($"{request.DisplayViewer} cleared all the plants !"),
                            null, true, "plantshealth");
                        }
                        break;
                    }
                case "shufflecards":
                    {
                        AddressChain cards_ptr_ch = game_ch.Offset(0x15C);
                        if (cards_ptr_ch.GetInt() != 0 && !cards.Any())
                        {
                            AddressChain cards_ch = cards_ptr_ch.Follow();
                            int ncards = cards_ch.Offset(0x24).GetInt();

                            TryEffect(request,
                            () => true,
                            () =>
                            {
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards.Add(cards_ch.Offset(0x5C + i * 0x50).GetInt());
                                }
                                cards = cards.OrderBy(a => RNG.Next()).ToList();
                                for (int i = 0; i < ncards; i++)
                                {
                                    cards_ch.Offset(0x5C + i * 0x50).SetInt(cards[i]);
                                }
                                cards.Clear();
                                return true;
                            },
                            () => Connector.SendMessage($"{request.DisplayViewer} shuffled all your cards !"),
                            null, true, "cards");
                        }
                        break;
                    }
                default:
                    Log.Message("Unsupported effect " + code[0]);
                    break;
            }
        }

        protected override bool StopEffect(EffectRequest request)
        {
            return true;
        }

        protected override void RequestData(DataRequest request) => Respond(request, request.Key, null, false, $"Variable name \"{request.Key}\" not known.");

        protected short GetShort(AddressChain ch)
        {
            return BitConverter.ToInt16(ch.GetBytes(2), 0);
        }
        protected void SetShort(AddressChain ch, short value)
        {
            ch.SetBytes(BitConverter.GetBytes(value));
        }
        protected ushort GetUShort(AddressChain ch)
        {
            return BitConverter.ToUInt16(ch.GetBytes(2), 0);
        }
        protected void SetUShort(AddressChain ch, ushort value)
        {
            ch.SetBytes(BitConverter.GetBytes(value));
        }

        protected int random_percentage(int value)
        {
            return (int)((value / 100.0f) * RNG.Next(MIN_PERCENTAGE, MAX_PERCENTAGE));
        }

        protected int random_big_percentage(int value)
        {
            return (int)((value / 100.0f) * RNG.Next(MIN_BIG_PERCENTAGE, MAX_BIG_PERCENTAGE));
        }

        protected long ida_addr(long addr)
        {
            return (addr - imagebase) + 0x400000;
        }

        protected bool is_not_paused()
        {
            return game_ch.Offset(0x67).GetByte() == 1;
        }
        byte[] int_to_array_little_endian(long data)
        {
            byte[] b = new byte[4];
            b[0] = (byte)data;
            b[1] = (byte)(((long)data >> 8) & 0xFF);
            b[2] = (byte)(((long)data >> 16) & 0xFF);
            b[3] = (byte)(((long)data >> 24) & 0xFF);
            return b;
        }

        bool is_one_zombie_in_visible_range(AddressChain active_zombies_ch, int nactive_zombies)
        {
            bool res = false;
            AddressChain tmp_ch = active_zombies_ch;
            for (int i = 0; i < nactive_zombies; i++)
            {
                if (tmp_ch.Offset(0x8).GetInt() <= MIN_VISIBLE_X)
                {
                    res = true;
                    break;
                }
                tmp_ch = tmp_ch.Offset(ZOMBIE_OBJECT_SIZE);
            }
            return res;
        }

        bool is_zombie_out()
        {
            bool res = false;
            AddressChain active_zombies_ptr = game_ch.Offset(0xA8);
            int nactive_zombies = game_ch.Offset(0xAC).GetInt();
            if (active_zombies_ptr.GetInt() != 0 && nactive_zombies > 0)
            {
                res = is_one_zombie_in_visible_range(active_zombies_ptr.Follow(), nactive_zombies);
            }
            return res;
        }
    }
}
