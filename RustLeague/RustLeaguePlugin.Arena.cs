using System;
using System.Collections.Generic;
using System.Globalization;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace RustLeagueHarmony
{
    public partial class RustLeaguePlugin
    {
        public class ArenaBounds : MonoBehaviour
        {
            private BoxCollider boxCollider;

            private void Awake()
            {
                var rb = gameObject.GetComponent<Rigidbody>();
                if (rb != null) DestroyImmediate(rb);
                rb = gameObject.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.detectCollisions = true;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                boxCollider = gameObject.GetComponent<BoxCollider>();
                if (boxCollider == null)
                    boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                Vector3 size = Instance.configData.eventSettings.ArenaBoundsSize;
                if (size == Vector3.zero) size = new Vector3(55f, 40f, 95f);
                boxCollider.size = size;
            }

            private void OnTriggerExit(Collider col)
            {
                BaseEntity baseEntity = col?.ToBaseEntity();
                if (!baseEntity.IsValid() || baseEntity != Instance.ball)
                    return;
                Instance.ballMono?.ReverseVelocity();
            }

            private void OnDestroy() => UnityEngine.Object.Destroy(gameObject);
        }

        public class golePostRed : MonoBehaviour
        {
            private BoxCollider boxCollider;

            private void Awake()
            {
                transform.position = Instance.configData.eventSettings.RedZone;
                transform.rotation = Quaternion.Euler(0f, Instance.configData.eventSettings.RedZoneRotation, 0f);
                gameObject.layer = 0;
                var rigidbody = gameObject.GetComponent<Rigidbody>();
                if (rigidbody != null) DestroyImmediate(rigidbody);
                rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

                boxCollider = gameObject.GetComponent<BoxCollider>();
                if (boxCollider == null)
                    boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = Instance.configData.eventSettings.RedZoneSize;
            }

            void OnDestroy() => UnityEngine.GameObject.Destroy(gameObject);

            public void Destory()
            {
                if (boxCollider != null) DestroyImmediate(boxCollider);
                UnityEngine.GameObject.Destroy(gameObject);
            }

            private void OnTriggerEnter(Collider col)
            {
                BaseEntity baseEntity = col?.ToBaseEntity();
                if (!baseEntity.IsValid() || baseEntity != Instance.ball)
                    return;
                Instance.ballMono.Score(false);
            }
        }

        public class golePostBlue : MonoBehaviour
        {
            private BoxCollider boxCollider;

            private void Awake()
            {
                transform.position = Instance.configData.eventSettings.BlueZone;
                transform.rotation = Quaternion.Euler(0f, Instance.configData.eventSettings.BlueZoneRotation, 0f);
                gameObject.layer = 0;
                var rigidbody = gameObject.GetComponent<Rigidbody>();
                if (rigidbody != null) DestroyImmediate(rigidbody);
                rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;

                boxCollider = gameObject.GetComponent<BoxCollider>();
                if (boxCollider == null)
                    boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.isTrigger = true;
                boxCollider.size = Instance.configData.eventSettings.BlueZoneSize;
            }

            void OnDestroy() => UnityEngine.GameObject.Destroy(gameObject);

            public void Destory()
            {
                if (boxCollider != null) DestroyImmediate(boxCollider);
                UnityEngine.GameObject.Destroy(gameObject);
            }

            private void OnTriggerEnter(Collider col)
            {
                BaseEntity baseEntity = col?.ToBaseEntity();
                if (!baseEntity.IsValid() || baseEntity != Instance.ball)
                    return;
                Instance.ballMono.Score(true);
            }
        }

        public class rustLeagueCar : MonoBehaviour
        {
            public ModularCar car;
            public string team;
            public BasePlayer driver;
            public bool canMove;
            public int rocketsShot;
            public int times = 2;
            public DateTime nextShot = DateTime.Now;
            private float _blastReady;
            private float _invertedSince = -1f;

            private void Awake()
            {
                car = GetComponent<ModularCar>();
                if (Instance != null && !Instance.LiveCars.Contains(this))
                    Instance.LiveCars.Add(this);
                Instance.timer.NextTick(() =>
                {
                    if (car == null || car.IsDestroyed) return;
                    car.AdminFixUp(Instance.configData.CarSettings.tierFixUp);
                    spawnAtachments();
                    DisablePersistence(car);
                });
                if (Instance.configData.CarSettings.carFrame.Contains("_2module")) times = 2;
                if (Instance.configData.CarSettings.carFrame.Contains("_3module")) times = 3;
                if (Instance.configData.CarSettings.carFrame.Contains("_4module")) times = 4;
            }

            private void OnDestroy()
            {
                Instance?.LiveCars.Remove(this);
            }

            private void Update()
            {
                if (!canMove && car?.rigidBody != null)
                {
                    car.rigidBody.velocity *= 0f;
                    car.rigidBody.angularVelocity *= 0f;
                }
                TickAutoRight();
                KeepOnPlayfield();
            }

            public bool IsSameCar(rustLeagueCar other)
            {
                if (other == null) return false;
                if (this == other) return true;
                return car != null && other.car != null && car == other.car;
            }

            private void TickAutoRight()
            {
                if (car == null || car.IsDestroyed) return;
                if (!car.IsFlipped())
                {
                    _invertedSince = -1f;
                    return;
                }
                if (_invertedSince < 0f)
                    _invertedSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _invertedSince < 0.8f) return;
                RightCar();
                _invertedSince = -1f;
            }

            private void RightCar()
            {
                var rb = car.rigidBody;
                if (rb == null) return;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Vector3 center = Instance.configData.eventSettings.eventCenter;
                Vector3 pos = car.transform.position;
                pos.y = center.y > 1f ? center.y : pos.y + 1.25f;
                Quaternion rot = Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f);
                car.transform.SetPositionAndRotation(pos, rot);
                rb.position = pos;
                rb.rotation = rot;
                SyncNow();
            }

            private void KeepOnPlayfield()
            {
                if (car == null || car.IsDestroyed) return;
                Vector3 center = Instance.configData.eventSettings.eventCenter;
                if (center.y < 1f) return;
                var rb = car.rigidBody;
                Vector3 p = car.transform.position;
                bool dirty = false;
                if (p.y < center.y - 1.5f)
                {
                    p.y = center.y;
                    dirty = true;
                    if (rb != null)
                    {
                        Vector3 v = rb.velocity;
                        v.y = 0f;
                        rb.velocity = v;
                    }
                }
                else if (p.y > center.y + 8f)
                {
                    p.y = center.y + 2f;
                    dirty = true;
                    if (rb != null)
                    {
                        Vector3 v = rb.velocity;
                        v.y = 0f;
                        rb.velocity = v;
                    }
                }
                if (!dirty) return;
                car.transform.position = p;
                if (rb != null) rb.position = p;
                SyncNow();
            }

            public void SyncNow()
            {
                if (car == null || car.IsDestroyed) return;
                if (car.rigidBody != null && car.rigidBody.IsSleeping())
                    car.rigidBody.WakeUp();
                car.transform.hasChanged = true;
                car.InvalidateNetworkCache();
                car.UpdateNetworkGroup();
                car.SendNetworkUpdateImmediate();
            }

            public void TryStartEngine()
            {
                if (car == null || car.IsDestroyed || driver == null) return;
                car.engineController?.TryStartEngine(driver);
            }

            private void spawnAtachments()
            {
                if (car == null) return;
                string prefab = team == "blue"
                    ? "assets/prefabs/io/electric/lights/sirenlightblue.prefab"
                    : "assets/prefabs/io/electric/lights/sirenlightorange.prefab";
                IOEntity lights = GameManager.server.CreateEntity(prefab) as IOEntity;
                if (lights == null) return;
                lights.enableSaving = false;
                lights.SetParent(car, 0);
                lights.transform.localPosition = new Vector3(0f, 2.10f, -0.5f);
                lights.transform.localRotation = Quaternion.identity;
                lights.Spawn();
                lights.UpdateFromInput(20, 0);
            }

            public void flipOver()
            {
                if (driver != null && Instance.ballMono != null && Instance.ballMono.notstarted)
                {
                    if (car.rigidBody != null)
                        car.rigidBody.velocity *= 0.2f;
                    car.DoPushAction(driver);
                }
            }

            public void ApplyRocketBlast(Vector3 blastPos, rustLeagueCar shooter = null)
            {
                if (IsSameCar(shooter)) return;
                if (car?.rigidBody == null) return;
                if (Time.realtimeSinceStartup < _blastReady) return;
                _blastReady = Time.realtimeSinceStartup + 0.2f;
                var rb = car.rigidBody;
                if (rb.IsSleeping()) rb.WakeUp();
                if (blastPos == Vector3.zero)
                    blastPos = car.transform.position;
                Vector3 delta = car.transform.position - blastPos;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.05f)
                    delta = car.transform.forward;
                delta.Normalize();
                Vector3 v = rb.velocity;
                v.x += delta.x * 10f;
                v.z += delta.z * 10f;
                v.y = Mathf.Clamp(v.y + 2f, -3f, 5f);
                rb.velocity = v;
                SyncNow();
            }

            public void FireRocket()
            {
                if (Instance.ballMono == null || !Instance.ballMono.notstarted || nextShot > DateTime.Now || rocketsShot >= Instance.configData.CarSettings.totalRockets)
                {
                    if (driver != null)
                        Instance.RunEffect(driver, driver.GetNetworkPosition(), "assets/prefabs/weapons/rocketlauncher/effects/dryfire.prefab");
                    return;
                }
                nextShot = DateTime.Now.AddSeconds(5);

                Vector3 dir = car.transform.forward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up);
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector3.forward;
                dir.Normalize();
                dir.y = -0.05f;
                dir.Normalize();

                Vector3 launchPos = car.transform.position + Vector3.up * 1.35f + dir * (times + 1.25f);
                Quaternion aim = Quaternion.LookRotation(dir, Vector3.up);
                BaseEntity rocket = GameManager.server.CreateEntity("assets/prefabs/ammo/rocket/rocket_hv.prefab", launchPos, aim, true);
                if (rocket == null) return;
                rocket.enableSaving = false;
                TimedExplosive rocketExplosion = rocket.GetComponent<TimedExplosive>();
                ServerProjectile rocketProjectile = rocket.GetComponent<ServerProjectile>();
                if (rocketExplosion != null)
                {
                    rocketExplosion.timerAmountMin = 60;
                    rocketExplosion.timerAmountMax = 60;
                    rocketExplosion.explosionRadius = 10f;
                    rocketExplosion.minExplosionRadius = 4f;
                }
                rocket.Spawn();
                if (driver != null)
                    rocket.creatorEntity = driver;
                var tracker = rocket.gameObject.AddComponent<LeagueRocket>();
                tracker.Shooter = this;
                if (rocketProjectile != null)
                {
                    rocketProjectile.speed = 40f;
                    rocketProjectile.gravityModifier = 0f;
                    rocketProjectile.InitializeVelocity(dir * 40f);
                }
                rocketsShot++;
                if (driver != null)
                    Instance.timer.Once(4f, () => Instance.RunEffect(driver, driver.GetNetworkPosition(), "assets/prefabs/weapons/rocketlauncher/effects/reload_insert_rocket.prefab"));
            }
        }

        public class LeagueRocket : MonoBehaviour
        {
            public rustLeagueCar Shooter;

            private void OnDestroy()
            {
                Vector3 pos = transform.position;
                var cars = Instance?.LiveCars;
                if (cars == null) return;
                for (int i = 0; i < cars.Count; i++)
                {
                    var ride = cars[i];
                    if (ride == null || ride.car == null || ride.IsSameCar(Shooter)) continue;
                    if ((ride.car.transform.position - pos).sqrMagnitude <= 64f)
                        ride.ApplyRocketBlast(pos, Shooter);
                }
            }
        }

        public class rustLeague : MonoBehaviour
        {
            public BaseEntity ball;
            public Rigidbody rb;
            public float Duration;
            public DateTime lastBlock = DateTime.Now;
            internal DateTime lastUINotification = DateTime.MinValue;
            public string notifyMessage = "  Round intermission";
            public string BlockName = "BlocknameTimer";
            public bool notstarted;
            public int currentRound = 1;
            public int redScore;
            public int blueScore;
            public bool playerSetup;
            public Vector3 center;
            public string blueNames = "";
            public string redNames = "";
            public Dictionary<ulong, bool> redPlayer = new Dictionary<ulong, bool>();
            public Dictionary<ulong, bool> bluePlayer = new Dictionary<ulong, bool>();
            public List<BasePlayer> allBasePlayers = new List<BasePlayer>();
            private float _scoreLockUntil;
            private int _countdownShown = -1;
            private Timer _hideMessages;

            private void Awake()
            {
                ball = GetComponent<BaseEntity>();
                rb = ball.GetComponent<Rigidbody>();
                Duration = 20f;
                center = Instance.configData.eventSettings.eventCenter;
                center.y += 1f;
                Instance.timer.Once(5f, () =>
                {
                    InvokeRepeating(nameof(isBallInZone), 4f, 5f);
                    takeFule(false);
                    GetMessageGUI(Instance.Lang("EventBegin"), 26, 13f);
                });
            }

            public void ReverseVelocity()
            {
                if (Instance.ball == null || rb == null) return;
                Vector3 euler = Instance.ball.transform.eulerAngles;
                Instance.ball.transform.rotation = Quaternion.Euler(euler.x, euler.y - 180f, euler.z);
                rb.velocity *= -1f;
            }

            public void Score(bool team)
            {
                if (!notstarted) return;
                if (Time.time < _scoreLockUntil) return;
                _scoreLockUntil = Time.time + 2.5f;
                if (!team)
                {
                    redScore++;
                    GetMessageGUI(Instance.Lang("RedScore"), 30, 2f, true, "255 0 0 1");
                    resetBall();
                    if (redScore >= Instance.configData.settings.WinPoints)
                        endWinEvent();
                }
                else
                {
                    blueScore++;
                    GetMessageGUI(Instance.Lang("BlueScore"), 30, 2f, true, "0 0 255 1");
                    resetBall();
                    if (blueScore >= Instance.configData.settings.WinPoints)
                        endWinEvent();
                }
            }

            public void CheckGoals()
            {
                if (ball == null || Time.time < _scoreLockUntil || !notstarted) return;
                Vector3 p = ball.transform.position;
                if (Instance.PointInGoal(p, true))
                    Score(false);
                else if (Instance.PointInGoal(p, false))
                    Score(true);
            }

            private void endWinEvent()
            {
                currentRound = Instance.configData.settings.MaxRounds * 2;
                lastBlock = DateTime.Now;
                lastUINotification = DateTime.MinValue;
                Duration = 0f;
            }

            public void isBallInZone()
            {
                if (Instance.arenaBounds == null || ball == null) return;
                var col = Instance.arenaBounds.GetComponent<Collider>();
                if (col == null) return;
                if (col.ClosestPoint(ball.transform.position) != ball.transform.position)
                    resetBall();
            }

            public void resetBall()
            {
                Instance.ball.transform.position = Instance.configData.eventSettings.eventCenter + Vector3.up * 5f;
                if (rb != null) { rb.velocity *= -1f; rb.useGravity = false; }
                Instance.ball.transform.hasChanged = true;
                Instance.ball.SendNetworkUpdateImmediate();
                Instance.timer.NextTick(() =>
                {
                    if (rb != null) { rb.velocity = Vector3.zero; rb.useGravity = true; }
                    Instance.ball.SendNetworkUpdateImmediate();
                });
            }

            private void takeFule(bool take)
            {
                var cars = Instance.LiveCars;
                for (int i = 0; i < cars.Count; i++)
                {
                    var McarControler = cars[i];
                    if (McarControler == null) continue;
                    McarControler.rocketsShot = 0;
                    McarControler.canMove = take;
                    if (!playerSetup)
                    {
                        if (McarControler.team == "blue" && McarControler.driver != null && !bluePlayer.ContainsKey(McarControler.driver.GetUserId()))
                        {
                            blueNames = blueNames + " " + McarControler.driver.displayName;
                            bluePlayer[McarControler.driver.GetUserId()] = false;
                            allBasePlayers.Add(McarControler.driver);
                        }
                        if (McarControler.team == "red" && McarControler.driver != null && !redPlayer.ContainsKey(McarControler.driver.GetUserId()))
                        {
                            redNames = redNames + " " + McarControler.driver.displayName;
                            redPlayer[McarControler.driver.GetUserId()] = false;
                            allBasePlayers.Add(McarControler.driver);
                        }
                    }
                    if (!take) McarControler.car?.AdminFixUp(Instance.configData.CarSettings.tierFixUp);
                    else McarControler.TryStartEngine();
                }
                playerSetup = true;
            }

            public bool Active
            {
                get
                {
                    if (lastBlock > DateTime.MinValue)
                        return (DateTime.Now - lastBlock).TotalSeconds < Duration;
                    return false;
                }
            }

            public void runInter(bool stop, float timeSet = 2f)
            {
                if (!stop)
                {
                    foreach (BasePlayer player in allBasePlayers)
                    {
                        if (player == null) continue;
                        CuiHelper.DestroyUi(player, "RtimerS" + BlockName);
                    }
                    return;
                }

                _hideMessages?.Destroy();
                _hideMessages = Instance.timer.Once(timeSet, () =>
                {
                    foreach (BasePlayer player in allBasePlayers)
                    {
                        if (player == null) continue;
                        CuiHelper.DestroyUi(player, "Messages" + BlockName);
                    }
                });
            }

            public void GetGUI()
            {
                foreach (BasePlayer player in allBasePlayers)
                {
                    if (player == null) continue;
                    CuiHelper.DestroyUi(player, "RtimerS" + BlockName);
                    SendGUI(player);
                }
            }

            public void GetMessageGUI(string messages, int fSize, float timeSet, bool sound = false, string colors = "255, 255, 255")
            {
                foreach (BasePlayer player in allBasePlayers)
                {
                    if (player == null) continue;
                    CuiHelper.DestroyUi(player, "Messages" + BlockName);
                    CuiHelper.DestroyUi(player, "Score" + BlockName);
                    Scoreboard(player);
                    SendMessageGUI(player, messages, fSize, timeSet, colors);
                    if (sound) Instance.RunEffect(player, player.GetNetworkPosition(), "assets/bundled/prefabs/fx/invite_notice.prefab");
                }
            }

            void Update()
            {
                CheckGoals();
                TickCountdown();
                if (rb != null)
                {
                    Vector3 max = Instance.configData.BallSettings.BallMaxvelocity;
                    Vector3 v = rb.velocity;
                    if (v.x > max.x) v.x = max.x;
                    if (v.y > max.y) v.y = max.y;
                    if (v.z > max.z) v.z = max.z;
                    rb.velocity = v;
                }

                bool send = false;
                if (lastUINotification == DateTime.MinValue)
                {
                    lastUINotification = DateTime.Now;
                    send = true;
                }
                else if ((DateTime.Now - lastUINotification).TotalSeconds > 1)
                    send = true;

                if (!Active)
                {
                    runInter(false);
                    if (notstarted)
                    {
                        if (currentRound >= Instance.configData.settings.MaxRounds)
                        {
                            string winner = "tie";
                            Instance.finalScore = redScore + "-" + blueScore;
                            if (redScore > blueScore) { winner = "red"; Instance.finalScore = redScore + "-" + blueScore; }
                            else if (redScore < blueScore) { winner = "blue"; Instance.finalScore = blueScore + "-" + redScore; }
                            if (notifyMessage.Contains("Over"))
                            {
                                Instance.closeEvent(winner);
                                return;
                            }
                            notifyMessage = "  Event Over";
                            lastBlock = DateTime.Now;
                            lastUINotification = DateTime.MinValue;
                            Duration = 5f;
                            Instance.resetRound();
                            GetMessageGUI(Instance.Lang(winner + "GUI"), 30, 4f);
                            currentRound++;
                            return;
                        }
                        notstarted = false;
                        takeFule(false);
                        GetMessageGUI(Instance.Lang("RoundOver"), 30, 15f);
                        Instance.resetRound();
                        currentRound++;
                        notifyMessage = "  Round intermission";
                        lastBlock = DateTime.Now;
                        lastUINotification = DateTime.MinValue;
                        _countdownShown = -1;
                        Duration = 20f;
                    }
                    else
                    {
                        notstarted = true;
                        resetBall();
                        takeFule(true);
                        GetMessageGUI(Instance.Lang("startEngines"), 30, 2f);
                        notifyMessage = "  Round " + currentRound + " will end in";
                        lastBlock = DateTime.Now;
                        lastUINotification = DateTime.MinValue;
                        Duration = Instance.configData.settings.RoundSeconds;
                    }
                }

                if (send && Active)
                {
                    lastUINotification = DateTime.Now;
                    GetGUI();
                }
            }

            private void TickCountdown()
            {
                if (!Active || string.IsNullOrEmpty(notifyMessage)
                    || notifyMessage.IndexOf("intermission", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _countdownShown = -1;
                    return;
                }

                double left = (lastBlock.AddSeconds(Duration) - DateTime.Now).TotalSeconds;
                int sec = (int)Math.Floor(left);
                if (sec > 3 || sec < 0) return;
                if (sec == _countdownShown) return;
                _countdownShown = sec;
                if (sec >= 1 && sec <= 3)
                    GetMessageGUI(sec.ToString(), 100, 1.2f, false, "1 1 1 1");
            }

            private string FormatTime(TimeSpan ts)
            {
                if (ts.Days > 0) return string.Format("{0}D, {1}H", ts.Days, ts.Hours);
                if (ts.Hours > 0) return string.Format("{0}H {1}M", ts.Hours, ts.Minutes);
                return string.Format("{0}M {1}S", ts.Minutes, ts.Seconds);
            }

            void SendMessageGUI(BasePlayer current, string messages, int fSize, float timeSet, string colors = "255, 255, 255")
            {
                if (current == null) return;
                CuiHelper.DestroyUi(current, "Messages" + BlockName);
                var elements = new CuiElementContainer();
                var BlockMsg = elements.Add(new CuiPanel
                {
                    Image = { Color = "255, 0, 0, 0.0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, "Hud", "Messages" + BlockName);
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Color = colors, Text = messages, FontSize = fSize, Align = TextAnchor.MiddleCenter }
                }, BlockMsg);
                CuiHelper.AddUi(current, elements);
                runInter(true, timeSet);
            }

            void Scoreboard(BasePlayer current)
            {
                if (current == null) return;
                CuiHelper.DestroyUi(current, "Score" + BlockName);
                string AnchorMinRed = "0.03 0.12";
                string AnchorMaxRed = "0.97 0.46";
                string AnchorMinBlue = "0.03 0.48";
                string AnchorMaxBlue = "0.97 0.82";
                string scoreRed = "RED  " + redScore;
                string scoreBlue = "BLUE  " + blueScore;
                string myTeam = Instance.GetPlayerTeam(current);
                if (myTeam == "red")
                    scoreRed = "YOU  RED  " + redScore;
                if (myTeam == "blue")
                    scoreBlue = "YOU  BLUE  " + blueScore;
                var elements = new CuiElementContainer();
                var BlockMsg = elements.Add(new CuiPanel
                {
                    Image = ChaosImage(ChaosBg),
                    RectTransform = { AnchorMin = "0.012 0.78", AnchorMax = "0.180 0.89" }
                }, "Hud", "Score" + BlockName);
                if (myTeam == "red")
                {
                    elements.Add(new CuiPanel
                    {
                        Image = { Color = "0.55 0.10 0.07 0.55" },
                        RectTransform = { AnchorMin = "0 0.12", AnchorMax = "1 0.46" }
                    }, BlockMsg);
                }
                else if (myTeam == "blue")
                {
                    elements.Add(new CuiPanel
                    {
                        Image = { Color = "0.05 0.22 0.55 0.55" },
                        RectTransform = { AnchorMin = "0 0.48", AnchorMax = "1 0.82" }
                    }, BlockMsg);
                }
                elements.Add(new CuiPanel
                {
                    Image = ChaosImage(ChaosHeader, true),
                    RectTransform = { AnchorMin = "0 0.82", AnchorMax = "1 1" }
                }, BlockMsg);
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.04 0.82", AnchorMax = "0.96 1" },
                    Text = { Color = ChaosText, Text = "RUST LEAGUE", FontSize = 11, Align = TextAnchor.MiddleLeft, Font = ChaosFontTitle }
                }, BlockMsg);
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = AnchorMinRed, AnchorMax = AnchorMaxRed },
                    Text = { Color = ChaosRed, Text = scoreRed, FontSize = 16, Align = TextAnchor.MiddleLeft, Font = ChaosFontBody }
                }, BlockMsg);
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.04 0", AnchorMax = "0.96 0.14" },
                    Text = { Color = ChaosMuted, Text = redNames, FontSize = 8, Align = TextAnchor.UpperLeft, Font = ChaosFontReg }
                }, BlockMsg);
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = AnchorMinBlue, AnchorMax = AnchorMaxBlue },
                    Text = { Color = ChaosBlue, Text = scoreBlue, FontSize = 16, Align = TextAnchor.MiddleLeft, Font = ChaosFontBody }
                }, BlockMsg);
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.04 0.46", AnchorMax = "0.96 0.56" },
                    Text = { Color = ChaosMuted, Text = blueNames, FontSize = 8, Align = TextAnchor.UpperLeft, Font = ChaosFontReg }
                }, BlockMsg);
                CuiHelper.AddUi(current, elements);
                Instance.ShowTeamHud(current, myTeam);
            }

            void SendGUI(BasePlayer current)
            {
                TimeSpan ts = lastBlock.AddSeconds(Duration) - DateTime.Now;
                string countDown = FormatTime(ts);
                CuiHelper.DestroyUi(current, "RtimerS" + BlockName);
                var elements = new CuiElementContainer();
                var BlockMsg = elements.Add(new CuiPanel
                {
                    Image = ChaosImage(ChaosBg),
                    RectTransform = { AnchorMin = "0.012 0.90", AnchorMax = "0.180 0.955" }
                }, "Hud", "RtimerS" + BlockName);
                elements.Add(new CuiElement
                {
                    Parent = BlockMsg,
                    Components =
                    {
                        new CuiRawImageComponent { Sprite = "assets/icons/explosion.png", Color = ChaosGreen },
                        new CuiRectTransformComponent { AnchorMin = "0.02 0.12", AnchorMax = "0.13 0.88" }
                    }
                });
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0.15 0", AnchorMax = "0.70 1" },
                    Text = { Color = ChaosText, Text = notifyMessage, FontSize = 11, Align = TextAnchor.MiddleLeft, Font = ChaosFontBody }
                }, BlockMsg);
                elements.Add(new CuiElement
                {
                    Name = "TimerPanel",
                    Parent = BlockMsg,
                    Components =
                    {
                        new CuiImageComponent { Color = ChaosHeader, Sprite = ChaosSprite, ImageType = Image.Type.Tiled },
                        new CuiRectTransformComponent { AnchorMin = "0.70 0.12", AnchorMax = "0.98 0.88" }
                    }
                });
                elements.Add(new CuiLabel
                {
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Color = ChaosText, Text = countDown, FontSize = 11, Align = TextAnchor.MiddleCenter, Font = ChaosFontBody }
                }, "TimerPanel");
                CuiHelper.AddUi(current, elements);
                Instance.ShowTeamHud(current);
            }
        }

        public void openJoinWindow(BasePlayer player, bool inEvent = false) => theUIleagueMenu(player, inEvent);

        private const string ChaosBg = "0.082 0.082 0.082 0.95";
        private const string ChaosHeader = "0.770 1.000 0.000 0.314";
        private const string ChaosButton = "0.165 0.180 0.192 1";
        private const string ChaosRed = "0.808 0.259 0.169 1";
        private const string ChaosGreen = "0.770 1.000 0.000 1";
        private const string ChaosBlue = "0.000 0.559 1.000 1";
        private const string ChaosText = "1 1 1 1";
        private const string ChaosMuted = "1 1 1 0.70";
        private const string ChaosSprite = "assets/content/ui/ui.background.rounded.png";
        private const string ChaosSpriteTop = "assets/content/ui/ui.background.rounded.top.png";
        private const string ChaosBlur = "assets/content/ui/uibackgroundblur.mat";
        private const string ChaosFontTitle = "PermanentMarker.ttf";
        private const string ChaosFontBody = "RobotoCondensed-Bold.ttf";
        private const string ChaosFontReg = "RobotoCondensed-Regular.ttf";

        private static CuiImageComponent ChaosImage(string color, bool header = false)
        {
            return new CuiImageComponent
            {
                Color = color,
                Sprite = header ? ChaosSpriteTop : ChaosSprite,
                Material = header ? null : ChaosBlur,
                ImageType = Image.Type.Tiled
            };
        }

        private void theUIleagueMenu(BasePlayer player, bool inEvent = false)
        {
            CuiHelper.DestroyUi(player, "theUIleagueMenu");
            string message = Lang("joinInfoUI");
            string joinEvent = Lang("UiJoin");
            string leaveEvent = Lang("UiLeave");
            string theCommand = "cui.endtest RUSTLEAGUE join";
            if (inEvent)
            {
                message = Lang("leaveInfoUI");
                joinEvent = Lang("UiLeave");
                leaveEvent = Lang("UiStay");
                theCommand = "cui.endtest RUSTLEAGUE leave";
            }

            var elements = new CuiElementContainer();
            var ConfigMenu = elements.Add(new CuiPanel
            {
                Image = ChaosImage(ChaosBg),
                RectTransform = { AnchorMin = "0.34 0.32", AnchorMax = "0.66 0.70" },
                CursorEnabled = true
            }, "Overlay", "theUIleagueMenu");

            elements.Add(new CuiPanel
            {
                Image = ChaosImage(ChaosHeader, true),
                RectTransform = { AnchorMin = "0 0.86", AnchorMax = "1 1" }
            }, ConfigMenu);
            elements.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.04 0.86", AnchorMax = "0.82 1" },
                Text = { Color = ChaosText, Text = "RUST LEAGUE", FontSize = 18, Align = TextAnchor.MiddleLeft, Font = ChaosFontTitle }
            }, ConfigMenu);
            elements.Add(new CuiButton
            {
                Button = { Close = ConfigMenu, Color = ChaosRed, Sprite = ChaosSprite, ImageType = Image.Type.Tiled },
                RectTransform = { AnchorMin = "0.88 0.875", AnchorMax = "0.985 0.985" },
                Text = { Text = "X", Color = ChaosText, FontSize = 14, Align = TextAnchor.MiddleCenter, Font = ChaosFontBody }
            }, ConfigMenu);
            elements.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.06 0.24", AnchorMax = "0.94 0.82" },
                Text = { Color = ChaosMuted, Text = message, FontSize = 14, Align = TextAnchor.UpperLeft, Font = ChaosFontReg }
            }, ConfigMenu);
            elements.Add(new CuiButton
            {
                Button = { Command = theCommand, Close = ConfigMenu, Color = ChaosHeader, Sprite = ChaosSprite, ImageType = Image.Type.Tiled },
                RectTransform = { AnchorMin = "0.06 0.06", AnchorMax = "0.48 0.20" },
                Text = { Text = joinEvent, Color = ChaosText, FontSize = 15, Align = TextAnchor.MiddleCenter, Font = ChaosFontBody }
            }, ConfigMenu);
            elements.Add(new CuiButton
            {
                Button = { Close = ConfigMenu, Color = ChaosRed, Sprite = ChaosSprite, ImageType = Image.Type.Tiled },
                RectTransform = { AnchorMin = "0.52 0.06", AnchorMax = "0.94 0.20" },
                Text = { Text = leaveEvent, Color = ChaosText, FontSize = 15, Align = TextAnchor.MiddleCenter, Font = ChaosFontBody }
            }, ConfigMenu);
            CuiHelper.AddUi(player, elements);
        }

        void CloseJoinList()
        {
            foreach (var ids in eventPlayer)
            {
                BasePlayer player = BasePlayer.FindByID(ids.Key);
                if (player == null) continue;
                CuiHelper.DestroyUi(player, "waitingPlay");
            }
        }

        void refreshJoinList(BasePlayer current = null)
        {
            if (current != null)
                CuiHelper.DestroyUi(current, "waitingPlay");
            foreach (var ids in eventPlayer)
            {
                BasePlayer player = BasePlayer.FindByID(ids.Key);
                if (player == null) continue;
                CuiHelper.DestroyUi(player, "waitingPlay");
                waitPlayersList(player);
            }
        }

        void waitPlayersList(BasePlayer current)
        {
            if (current == null) return;
            CuiHelper.DestroyUi(current, "waitingPlay");
            var elements = new CuiElementContainer();
            var BlockMsg = elements.Add(new CuiPanel
            {
                Image = ChaosImage(ChaosBg),
                RectTransform = { AnchorMin = "0.012 0.70", AnchorMax = "0.195 0.985" }
            }, "Hud", "waitingPlay");
            elements.Add(new CuiPanel
            {
                Image = ChaosImage(ChaosHeader, true),
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 1" }
            }, BlockMsg);
            elements.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0.04 0.88", AnchorMax = "0.96 1" },
                Text = { Color = ChaosText, Text = "WAITING LIST", FontSize = 13, Align = TextAnchor.MiddleLeft, Font = ChaosFontTitle }
            }, BlockMsg);

            float yMax = 0.86f;
            float row = 0.075f;
            int shown = 0;
            foreach (var ids in eventPlayer)
            {
                if (shown >= 10) break;
                BasePlayer player = BasePlayer.FindByID(ids.Key);
                if (player == null) continue;
                float yMin = yMax - row;
                elements.Add(new CuiLabel
                {
                    RectTransform =
                    {
                        AnchorMin = "0.06 " + yMin.ToString("0.###", CultureInfo.InvariantCulture),
                        AnchorMax = "0.94 " + yMax.ToString("0.###", CultureInfo.InvariantCulture)
                    },
                    Text = { Color = ChaosText, Text = player.displayName, FontSize = 12, Align = TextAnchor.MiddleLeft, Font = ChaosFontReg }
                }, BlockMsg);
                yMax = yMin;
                shown++;
            }
            CuiHelper.AddUi(current, elements);
        }

        private static Color FromHexString(string hexString)
        {
            if (string.IsNullOrEmpty(hexString))
                throw new InvalidOperationException("Cannot convert an empty/null string.");
            var str = hexString.Trim('#');
            switch (str.Length)
            {
                case 3:
                    str = new string(new[] { str[0], str[0], str[1], str[1], str[2], str[2], 'F', 'F' });
                    break;
                case 4:
                    str = new string(new[] { str[0], str[0], str[1], str[1], str[2], str[2], str[3], str[3] });
                    break;
                default:
                    if (str.Length < 6) str = str.PadRight(6, '0');
                    if (str.Length < 8) str = str.PadRight(8, 'F');
                    break;
            }
            var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
            var a = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);
            return new Color32(r, g, b, a);
        }

        public static class ColorExtensions
        {
            public static string ToRustFormatString(Color color)
            {
                return string.Format("{0:F2} {1:F2} {2:F2} {3:F2}", color.r, color.g, color.b, color.a);
            }

            public static bool TryParseHexString(string hexString, out Color color)
            {
                try
                {
                    color = FromHexString(hexString);
                    return true;
                }
                catch
                {
                    color = Color.white;
                    return false;
                }
            }
        }
    }
}
