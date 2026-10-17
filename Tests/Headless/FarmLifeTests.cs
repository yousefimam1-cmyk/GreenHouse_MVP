// Farm life features (Assets/Scripts/FarmLife): farm level + XP, crop mastery, roadside stand orders, friendships,
// festivals with contests, daily gift streak, farm beauty rating, photo mode and the journal. Runs inside the main
// headless world (called from Program.Main), plus checks after a restart and after a new game.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

static class FarmLifeTests
{
    static int levelBefore, heartsVet, filledBefore, ribbonsBefore, strawberryStars;
    static float xpBefore;

    static void PressKey(KeyControl k) { k.wasPressedThisFrame = true; k.isPressed = true; FakeEngine.Tick(0.02f); k.wasPressedThisFrame = false; k.isPressed = false; }
    static void PressPad(ButtonControl b) { b.wasPressedThisFrame = true; b.isPressed = true; FakeEngine.Tick(0.02f); b.wasPressedThisFrame = false; b.isPressed = false; }
    static bool NoEnglish(string s) => !System.Text.RegularExpressions.Regex.IsMatch(s ?? "", "[A-Za-z]{4,}");
    static string Hud() => string.Join("\n", HudLines.Status.Select(f => f()).Where(s => !string.IsNullOrEmpty(s)));
    static void GoTo(int day) { int guard = 0; while (WorldClock.Instance.Day < day && guard++ < 400) WorldClock.Instance.NextDay(); FakeEngine.Run(0.1f); }

    public static void Run()
    {
        Console.WriteLine("Farm life (level, mastery, stand, friends, festivals, daily gift, beauty, photo mode)");
        FarmLife.PassiveBonuses = true;
        Loc.SetLanguage(Language.English);
        var eco = EconomyManager.Instance;
        eco.AddMoney(20000);
        int errs0 = FakeEngine.Errors.Count;
        var data = FarmLife.Data;
        T.Check("FarmLife: systems created", FarmLevel.Instance && CropMastery.Instance && RoadsideStand.Instance && Friendship.Instance && Festivals.Instance
            && DailyGift.Instance && FarmBeauty.Instance && PhotoMode.Instance && FarmLifeScreen.Instance);
        T.Check("FarmLife: farmlife.json loaded (14 levels, 13 decor, 6 villagers, 5 festivals, 7-day gifts)",
            data.levels.Length == 14 && data.decor.Length == 13 && data.villagers.Length == 6 && data.festivals.Length == 5 && data.login.Length == 7 && data.orders.templates.Length >= 5,
            data.levels.Length + " " + data.decor.Length + " " + data.villagers.Length + " " + data.festivals.Length);

        // ---- every text has Arabic: code keys (Loc pack 96_fun.json) and bilingual data
        var missing = new List<string>();
        var src = Path.Combine(FakeEngine.ResourcesRoot, "..", "Scripts", "FarmLife");
        foreach (var f in Directory.GetFiles(src, "*.cs"))
        {
            string s = File.ReadAllText(f);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, "(?:Loc|UiText)\\.(?:T|Raw)\\(\"((?:[^\"\\\\]|\\\\.)*)\"")) if (!Loc.HasTranslation(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
        }
        foreach (var k in PhotoMode.FilterNames.Concat(new[] { "Farm decor", "Reach a higher farm level first", "Roadside stand", "Overview", "Friends", "Calendar", "Crop mastery", "Level unlocks", "Farm life journal", "Photo mode" }))
            if (!Loc.HasTranslation(k)) missing.Add(k);
        T.Check("FarmLife: every code text has Arabic (Loc/96_fun.json)", missing.Count == 0, string.Join(" | ", missing.Distinct()));
        Func<string, string, bool> Bi = (en, ar) => !string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(ar) && NoEnglish(ar);
        var bad = new List<string>();
        foreach (var v in data.villagers)
        {
            if (!Bi(v.en, v.ar)) bad.Add(v.id);
            if (v.talk.Any(t => !Bi(t.en, t.ar)) || v.events.Any(e => !Bi(e.en, e.ar))) bad.Add(v.id + " lines");
        }
        foreach (var d in data.decor) if (!Bi(d.en, d.ar)) bad.Add(d.id);
        foreach (var f in data.festivals) if (!Bi(f.en, f.ar) || !Bi(f.blurbEn, f.blurbAr) || (f.HasCompetition && !Bi(f.competition.en, f.competition.ar))) bad.Add(f.id);
        foreach (var t in data.orders.templates) if (!Bi(t.en, t.ar)) bad.Add(t.id);
        foreach (var n in data.orders.names.Concat(data.rivals)) if (!Bi(n.en, n.ar)) bad.Add(n.en);
        foreach (var f in data.features) if (!Bi(f.en, f.ar)) bad.Add(f.id);
        foreach (var c in data.mastery.crops) if (!Bi(c.en, c.ar)) bad.Add(c.id);
        T.Check("FarmLife: all data texts are bilingual (villagers, talk, heart events, decor, festivals, orders)", bad.Count == 0, string.Join(", ", bad));
        var goods = new HashSet<string>(eco.Data.goods.Select(g => g.id));
        var unknown = data.orders.templates.SelectMany(t => t.goods).Concat(data.villagers.SelectMany(v => v.loves.Concat(v.likes)))
            .Concat(data.festivals.Where(f => f.HasCompetition && f.competition.kind == "good").Select(f => f.competition.good)).Where(g => !goods.Contains(g)).Distinct().ToList();
        T.Check("FarmLife: order, gift and contest goods exist in the economy", unknown.Count == 0, string.Join(", ", unknown));

        // ---- farm level + XP
        var lvl = FarmLevel.Instance;
        T.Check("Level XP table: L2 = 100, L15 = 9,200, grows after the table", FarmLevel.LevelXp(1) == 0f && FarmLevel.LevelXp(2) == 100f && FarmLevel.LevelXp(15) == 9200f
            && FarmLevel.LevelXp(16) > 9200f && FarmLevel.LevelXp(17) - FarmLevel.LevelXp(16) > FarmLevel.LevelXp(16) - FarmLevel.LevelXp(15), FarmLevel.LevelXp(16) + " " + FarmLevel.LevelXp(17));
        T.Check("XP earned by the earlier play (harvests, sales, missions...)", lvl.Xp > 100f && lvl.Level >= 2, lvl.Xp + " xp, level " + lvl.Level);
        lvl.ClaimPending();
        float x0 = lvl.Xp;
        GameEvents.RaiseCustom("dailyQuestDone", 1f);
        GameEvents.RaiseDelivered(10f, 500);
        GameEvents.RaiseCustom("catchFish", 1f);
        T.Check("XP rates from farmlife.json (daily quest 20, 0.02 per SAR sold, fish 5)", lvl.Rate("dailyQuestDone") == 20f && Math.Abs(lvl.Rate("deliverMoney") - 0.02f) < 1e-6f && lvl.Rate("catchFish") == 5f);
        T.Check("XP from actions (quest + 500 SAR sale + fish >= 35; other systems may add more)", lvl.Xp - x0 >= 35f - 0.01f, (lvl.Xp - x0) + "");
        int L = lvl.Level;
        int money = eco.Money;
        int cards = CelebrationCard.History.Count;
        lvl.AddXp(FarmLevel.LevelXp(L + 1) - lvl.Xp + 0.5f);
        var row = FarmLevel.Row(L + 1);
        T.Check("Level up: level +1, celebration card, reward waiting (not paid yet)", lvl.Level == L + 1 && lvl.Pending.Contains(L + 1) && eco.Money == money
            && CelebrationCard.History.Skip(cards).Any(h => h.Contains("Farm level up!") && h.Contains("Level " + (L + 1))) && CelebrationCard.Instance != null, string.Join(" | ", CelebrationCard.History.Skip(cards)));
        T.Check("HUD shows level and reward ready", Hud().Contains("Farm level " + (L + 1)) && Hud().Contains("reward ready"), Hud());
        float feed0 = eco.Feed;
        string claimed = lvl.ClaimPending();
        // a mission or daily quest may pay at the same moment (money, harvest...), so the level reward is read from what the claim granted
        int reward = row != null ? row.money : FarmLevel.AutoReward(L + 1).money;
        T.Check("Collect the level reward (coins + feed / seeds)", lvl.Pending.Count == 0 && FarmLife.LastGrantMoney == reward && eco.Money >= money + reward && claimed.Contains("Level " + (L + 1)), claimed + " money " + money + " -> " + eco.Money + " mission " + MissionManager.Instance?.Current?.id + " quests " + string.Join(",", DailyQuests.Instance.Quests.Select(q => q.type + (q.done ? "*" : ""))));
        int money2 = eco.Money;
        T.Check("Nothing to collect twice", lvl.ClaimPending() == "" && eco.Money == money2, money2 + " -> " + eco.Money);
        // level-gated decor: the pigeon tower (level 8)
        var tower = FarmLife.Decor("fl_dovecote");
        var dec = Decorations.Instance;
        T.Check("Farm decor page in the shop (7 pieces sold, prizes never sold)", ShopCatalog.Entries.Count(e => e.category == "Farm decor") == 7 && ShopCatalog.Find("fl_trophy_palm") == null);
        T.Set(lvl, "state", new FarmLevelSave());   // back to level 1 so the unlock checks are exact
        T.Check("Locked decor can't be bought before its level", !FarmLevel.IsUnlocked("fl_dovecote") && !eco.TryBuy("fl_dovecote") && ShopCatalog.Find("fl_dovecote").detail().Contains("Level 8"), lvl.Level + "");
        lvl.AddXp(FarmLevel.LevelXp(8) - lvl.Xp + 1f);
        T.Check("Several levels at once: all rewards waiting", lvl.Level == 8 && lvl.Pending.Count == 7 && lvl.Pending.Contains(8), lvl.Level + " " + lvl.Pending.Count);
        T.Check("Unlocks follow the level (pigeon tower, 3 stand visitors)", FarmLevel.IsUnlocked("fl_dovecote") && FarmLevel.IsUnlocked("orders_slot3") && !FarmLevel.IsUnlocked("fl_door") && RoadsideStand.SlotCount >= 3);
        int stock = dec.Stock("fl_dovecote");
        T.Check("Buy the unlocked pigeon tower -> placing preview", eco.TryBuy("fl_dovecote") && dec.Stock("fl_dovecote") == stock + 1 && dec.Placing == "fl_dovecote");
        dec.CancelPlacing();
        lvl.ClaimPending();
        levelBefore = lvl.Level; xpBefore = lvl.Xp;

        // ---- crop mastery
        var cm = CropMastery.Instance;
        T.Set(cm, "state", new MasterySave());   // start from zero (earlier play already harvested some crops)
        int c0 = CelebrationCard.History.Count;
        T.Check("Mastery: 50 kg strawberries = no star yet", cm.Count("strawberry", 50f) == 0 && cm.Stars("strawberry") == 0);
        float xpM = lvl.Xp;
        T.Check("Mastery: 1st star at 100 kg (card + XP + badge)", cm.Count("strawberry", 60f) == 1 && CelebrationCard.History.Skip(c0).Any(h => h.Contains("Crop mastery")) && lvl.Xp - xpM >= 50f
            && cm.Badge("strawberry").Contains("*--") && cm.Badge("strawberry").Contains("next star at 400"), cm.Badge("strawberry"));
        T.Check("Mastery: 3 stars (1,000 kg) = mastered, +30% stand pay", cm.Count("strawberry", 900f) == 3 && cm.Badge("strawberry").Contains("Mastered") && Math.Abs(CropMastery.OrderBonus("strawberry") - 0.3f) < 0.001f);
        T.Check("Mastery: untracked crops ignored", cm.Count("rocks", 5000f) == 0 && !cm.Lines().Any(l => l.Contains("rocks")));
        strawberryStars = cm.Stars("strawberry");
        var bed = GameManager.Instance.Greenhouses.SelectMany(g => g.Cropbeds).First(b => b.Crop != null);
        string bedCrop = bed.Crop.id;
        cm.Count(bedCrop, Math.Max(0f, CropMastery.Thresholds(bedCrop)[0] - cm.Kg(bedCrop)) + 1f);
        FarmLife.PassiveBonuses = false;
        float s0 = eco.Stored("crops"); GameEvents.RaiseHarvested(bed, 100f); float plain = eco.Stored("crops") - s0;
        FarmLife.PassiveBonuses = true;
        s0 = eco.Stored("crops"); GameEvents.RaiseHarvested(bed, 100f); float bonus = eco.Stored("crops") - s0;
        int stars = cm.Stars(bedCrop);
        T.Check("Mastery: +5% harvest per star on " + bedCrop + " (" + stars + " stars)", stars >= 1 && Math.Abs(bonus - plain - 100f * stars * 0.05f) < 0.01f, plain + " vs " + bonus);
        T.Check("Mastery journal lines", cm.Lines().Count >= 2 && cm.Lines()[0].Contains("kg"));

        // ---- farm beauty
        var fb = FarmBeauty.Instance;
        fb.Compute();
        float score0 = fb.Score, decor0 = fb.DecorPart;
        dec.AddToStock("fl_fountain");
        T.Check("Prize / bought decor placed through Decorations", dec.PlaceAt("fl_fountain", CozyFx.FarmStart + new Vector3(-8, 0, 3), 0f) && GameObject.Find("Decor fl_fountain") != null
            && GameObject.Find("Decor fl_fountain").GetComponent<CozyProp>() != null);
        fb.Compute();
        T.Check("Beauty: the fountain adds 8 (decor part capped at 60)", Math.Abs(fb.DecorPart - Math.Min(60f, decor0 + 8f)) < 0.01f && fb.Score >= score0, decor0 + " -> " + fb.DecorPart);
        int plots = FieldManager.Instance.Plots.Count, tidy = FieldManager.Instance.Plots.Count(p => p.State != PlotState.Untilled);
        T.Check("Beauty: tidy fields part = 20 x worked plots / plots", Math.Abs(fb.FieldPart - 20f * tidy / plots) < 0.01f, fb.FieldPart + " " + tidy + "/" + plots);
        T.Check("Beauty: stars follow the thresholds", fb.StarCount == data.beauty.stars.Count(t => fb.Score >= t), fb.Score + " -> " + fb.StarCount);
        T.Check("Beauty bonus: +1% market prices and +5% stand pay per star (passive)", Math.Abs(FarmBeauty.PriceFor("wheat") - (1f + fb.StarCount * 0.01f)) < 0.0001f
            && Math.Abs(FarmBeauty.OrdersBonus - fb.StarCount * 0.05f) < 0.0001f);
        FarmLife.PassiveBonuses = false;
        T.Check("Passive bonuses off -> market untouched", FarmBeauty.PriceFor("wheat") == 1f);
        FarmLife.PassiveBonuses = true;
        T.Check("Beauty summary lists the parts", fb.Summary().Contains("Farm beauty") && fb.Summary().Contains("Tidy fields") && fb.Summary().Contains("Happy animals"));

        // ---- roadside stand
        var st = RoadsideStand.Instance;
        T.Check("Roadside stand built near the farmhouse", st.Stand != null && Vector3.Distance(st.Stand.transform.position, CozyFx.FarmStart) < 20f && st.Stand.GetComponent<CozyProp>() != null);
        FakeEngine.Run(1.2f);
        T.Check("Visitors waiting with named orders", st.ActiveCount >= 1 && st.Slots.Take(RoadsideStand.SlotCount).Where(o => o.active).All(o => o.lines.Count >= 1 && o.coins >= 20 && RoadsideStand.VisitorName(o).Length > 0));
        T.Check("A visitor stands at the stand per order", UnityEngine.Object.FindObjectsByType<CozyProp>(FindObjectsSortMode.None).Count(p => p.name.StartsWith("Stand visitor")) == st.ActiveCount);
        var gen = Enumerable.Range(1, 30).Select(i => st.Generate(i)).ToList();
        T.Check("Generated orders: 1-3 lines of real goods, coins and XP", gen.All(o => o.active && o.lines.Count >= 1 && o.lines.Count <= 3 && o.lines.All(l => eco.Good(l.good) != null && l.amount >= 1f) && o.coins >= 20 && o.xp > 0));
        T.Check("No festival orders outside festivals", Festivals.Instance.Today != null || gen.All(o => string.IsNullOrEmpty(o.festival)));
        st.Slots[0] = new StandOrder { active = true, template = "basket", nameIndex = 2, coins = 100, xp = 12, lines = new List<StandLine> { new StandLine { good = "wheat", amount = 5f } } };
        eco.TakeGood("wheat", eco.Stored("wheat"));
        T.Check("Not enough wheat -> can't serve", !RoadsideStand.CanFill(st.Slots[0]) && st.Fulfil(0) == 0 && st.Slots[0].active);
        eco.AddGood("wheat", 8f);
        int pay = RoadsideStand.Pay(st.Slots[0]);
        float expectedPay = 100f * (1f + FarmBeauty.OrdersBonus + CropMastery.OrderBonus("wheat"));
        money = eco.Money; float xpO = lvl.Xp; int filled = st.Filled;
        T.Check("Serve Noura's basket: wheat taken, coins (with bonuses) + XP paid", st.Fulfil(0) == pay && Math.Abs(pay - expectedPay) <= 1f && eco.Money == money + pay && Math.Abs(eco.Stored("wheat") - 3f) < 0.01f
            && lvl.Xp - xpO >= 12f && st.Filled == filled + 1 && !st.Slots[0].active, pay + " vs " + expectedPay);
        T.Check("Next visitor comes a few hours later", st.HoursUntil(0) > 1f && st.HoursUntil(0) <= 4.01f, st.HoursUntil(0) + "");
        st.Slots[0].arriveAt = FarmLife.Now - 0.01f;
        st.Tick();
        T.Check("...and arrives when due", st.Slots[0].active);
        T.Check("Send a visitor away", st.Skip(0) && !st.Slots[0].active && st.HoursUntil(0) <= 2.01f);
        filledBefore = st.Filled;

        // ---- friendships
        var fr = Friendship.Instance;
        FakeEngine.Run(0.5f);
        T.Check("6 villagers: 3 new ones spawned (vet, storyteller, young farmer) + the 3 cozy ones", fr.Npcs.Count == 6 && new[] { "vet", "elder", "youngfarmer" }.All(id => fr.Npcs[id] != null && fr.Npcs[id].GetComponent<CozyProp>() != null));
        T.Check("New villager prompt with hearts", fr.Npcs["vet"].GetComponent<CozyProp>().InteractionPrompt.Contains("Talk to Dr. Huda"), fr.Npcs["vet"].GetComponent<CozyProp>().InteractionPrompt);
        bool uiBefore = UIManager.Instance.IsUIOpened;
        Villagers.Instance.Npcs["buyer"].GetComponent<CozyProp>().Interact();
        T.Check("E on a cozy villager opens the talk / gift menu", fr.PanelOpen && fr.PanelId == "buyer" && UIManager.Instance.IsUIOpened && fr.List.Rows[0].label().Contains("Talk"));
        fr.Close();
        T.Check("Menu closes and gives input back", !fr.PanelOpen && UIManager.Instance.IsUIOpened == uiBefore);
        WorldClock.Instance.NextDay();
        int day = WorldClock.Instance.Day;
        while (Friendship.IsBirthday(Friendship.Def("vet"), WorldClock.Instance.Day) || Friendship.IsBirthday(Friendship.Def("vet"), WorldClock.Instance.Day + 1)) WorldClock.Instance.NextDay();
        string line = fr.Talk("vet");
        T.Check("Talk to Dr. Huda: line + 30 points, once a day", line.StartsWith("Dr. Huda:") && Math.Abs(fr.HeartProgress("vet") - 0.3f) < 0.001f && fr.TalkedToday("vet"), line + " " + fr.HeartProgress("vet"));
        fr.Talk("vet");
        T.Check("Second chat the same day adds nothing", Math.Abs(fr.HeartProgress("vet") - 0.3f) < 0.001f);
        eco.AddGood("camel_milk", 3f); eco.AddGood("eggs", 3f); eco.AddGood("laban", 3f);
        float cm0 = eco.Stored("camel_milk");
        T.Check("Loved gift (camel milk): +80 -> 1 heart, one unit taken", fr.Gift("vet", "camel_milk") == Friendship.Reaction.Loved && fr.Hearts("vet") == 1 && Math.Abs(eco.Stored("camel_milk") - cm0 + 1f) < 0.01f);
        T.Check("One gift a day", fr.Gift("vet", "eggs") == null && fr.Hearts("vet") == 1);
        T.Check("Gift reaction remembered", fr.KnownReaction("vet", "camel_milk") == "loves it");
        WorldClock.Instance.NextDay();
        float feedE = eco.Feed;
        int ev0 = CelebrationCard.History.Count;
        fr.Talk("vet"); fr.Gift("vet", "camel_milk");
        T.Check("2 hearts: heart event card + reward (feed +6)", fr.Hearts("vet") == 2 && fr.EventSeen("vet", 2) && eco.Feed >= feedE + 6f - 0.01f && CelebrationCard.History.Skip(ev0).Any(h => h.StartsWith("Dr. Huda")), fr.Hearts("vet") + " " + (eco.Feed - feedE));
        // birthday: gifts count triple
        int bd = Friendship.NextBirthday("vet", WorldClock.Instance.Day + 1);
        T.Check("Birthday on the calendar (spring 2)", bd > 0 && FarmLife.SeasonOf(bd) == Season.Spring && FarmLife.DayOfSeason(bd) == 2 && Festivals.Upcoming(40).Any(l => l.Contains("Birthday - Dr. Huda")));
        GoTo(bd);
        T.Check("Birthday shows on the HUD", Hud().Contains("Birthday today: Dr. Huda"), Hud());
        float before = fr.Hearts("vet") + fr.HeartProgress("vet");
        fr.Gift("vet", "eggs");
        float after = fr.Hearts("vet") + fr.HeartProgress("vet");
        T.Check("Birthday: liked gift (eggs) counts triple (+135 points)", Math.Abs(after - before - 1.35f) < 0.01f, before + " -> " + after);
        // the cozy neighbour: gifts add to Villagers' hearts
        var nb = Friendship.Def("neighbour");
        int h0 = Villagers.Hearts("neighbour");
        WorldClock.Instance.NextDay(); fr.Gift("neighbour", "laban");
        WorldClock.Instance.NextDay(); fr.Gift("neighbour", "laban");
        T.Check("Gifts raise the cozy villagers' hearts too (2 loved gifts = +1 heart)", Villagers.Hearts("neighbour") == Math.Min(5, h0 + 1) && nb.old, h0 + " -> " + Villagers.Hearts("neighbour"));
        fr.Open("elder");
        T.Check("Talk / gift menu lists Talk, gifts from the store and Close", fr.List.Rows.Count >= 3 && fr.List.Rows.Any(r => r.label().Contains("Give: Laban") || r.label().Contains("Give:")) && fr.List.Rows.Last().label() == "Close");
        fr.List.Choose(0);
        T.Check("Choosing Talk in the menu talks", fr.TalkedToday("elder") && fr.HeartProgress("elder") > 0f);
        fr.Close();
        heartsVet = fr.Hearts("vet");
        T.Check("Friends journal lines (hearts, birthday, loves)", fr.Lines().Count == 6 && fr.Lines().Any(l => l.Contains("Dr. Huda") && l.Contains("Loves") && l.Contains("Camel")), string.Join(" | ", fr.Lines()));

        // ---- festivals
        var fe = Festivals.Instance;
        int today = WorldClock.Instance.Day;
        foreach (var f in data.festivals)
        {
            int d = Festivals.NextDayOf(f.id, today);
            T.Check("Festival " + f.id + " is on the calendar and triggers on its day", d > 0 && Festivals.FestivalOn(d)?.id == f.id && (Festivals.FestivalOn(d - 1)?.id != f.id || !string.IsNullOrEmpty(f.linked)), d + "");
        }
        int ff = Festivals.NextDayOf("flower_fair", today);
        T.Check("Flower fair = spring day 3, Ramadan nights = Ramadan event days", FarmLife.SeasonOf(ff) == Season.Spring && FarmLife.DayOfSeason(ff) == 3
            && SeasonalEvents.EventOn(Festivals.NextDayOf("ramadan_nights", today))?.id == "ramadan");
        int md = Festivals.NextDayOf("melon_day", today);
        int fc = CelebrationCard.History.Count;
        GoTo(md);
        T.Check("Watermelon day starts: card, decorations, contest stand, HUD", fe.Today?.id == "melon_day" && fe.Decor != null && GameObject.Find("Festival Stand") != null
            && fe.Decor.GetComponentsInChildren<Transform>().Count(t => t.name == "Melon Pile") == 6 && CelebrationCard.History.Skip(fc).Any(h => h.Contains("Watermelon day")) && Hud().Contains("contest open"), Hud());
        eco.TakeGood("watermelon", eco.Stored("watermelon"));
        T.Check("Contest needs a watermelon in the store", fe.BlockReason() != null && fe.BlockReason().Contains("Watermelon") && fe.Enter() == 0);
        eco.AddGood("watermelon", 2f);
        money = eco.Money; int melonStock = dec.Stock("fl_melon_statue");
        int place = fe.Enter();
        var prize = place >= 1 && place <= 3 ? data.festivals.First(f => f.id == "melon_day").prizes[place - 1] : null;
        T.Check("Enter the biggest watermelon contest: placed 1-4, one melon used, prize by place", place >= 1 && place <= 4 && Math.Abs(eco.Stored("watermelon") - 1f) < 0.01f
            && eco.Money == money + (prize != null ? prize.money : 0) && dec.Stock("fl_melon_statue") == melonStock + (place == 1 ? 1 : 0) && fe.LastResult.Contains("kg"), place + " " + fe.LastResult);
        T.Check("Only one entry per festival per year", fe.Entered(fe.Today) && fe.Enter() == 0 && fe.BlockReason() == "Already entered this year");
        ribbonsBefore = fe.Ribbons;
        WorldClock.Instance.NextDay();
        T.Check("Festival decorations removed next day", fe.Today == null || fe.Today.id != "melon_day");
        int ram = Festivals.NextDayOf("ramadan_nights", WorldClock.Instance.Day);
        GoTo(ram);
        T.Check("Ramadan nights market with the Ramadan event", fe.Today?.id == "ramadan_nights" && SeasonalEvents.Instance.Active?.id == "ramadan");
        var iftar = Enumerable.Range(1, 40).Select(i => st.Generate(i)).Count(o => o.template == "iftar");
        T.Check("Iftar baskets ordered at the stand during Ramadan", iftar >= 10, iftar + "/40");
        int crescent = dec.Stock("fl_crescent");
        for (int i = 0; i < 3; i++)
        {
            st.Slots[0] = new StandOrder { active = true, template = "iftar", festival = "ramadan_nights", nameIndex = i, coins = 50, xp = 25, lines = new List<StandLine> { new StandLine { good = "laban", amount = 1f } } };
            eco.AddGood("laban", 1f);
            st.Fulfil(0);
        }
        T.Check("3 iftar baskets -> crescent lantern prize (once)", dec.Stock("fl_crescent") == crescent + 1 && fe.OrdersDone(fe.Today) == 3 && Hud().Contains("festival orders 3/3"), Hud());
        int eid = Festivals.NextDayOf("eid_show", WorldClock.Instance.Day);
        GoTo(eid);
        T.Check("Eid animal show follows Ramadan (needs animals: chickens in the pen)", fe.Today?.id == "eid_show" && SeasonalEvents.Instance.Active?.id == "eid" && (AnimalPen.Instance.AnimalCount == 0 ? fe.BlockReason() != null : fe.BlockReason() == null));
        if (AnimalPen.Instance.AnimalCount > 0) T.Check("Enter the Eid animal show", fe.Enter() >= 1 && fe.Ribbons >= ribbonsBefore);
        ribbonsBefore = fe.Ribbons;

        // ---- daily gift streak (real calendar days, account-wide)
        var dg = DailyGift.Instance;
        DailyGift.ResetStreak();
        var d0 = new DateTime(2026, 3, 10);
        DailyGift.Clock = () => d0;
        T.Check("Daily gift: first visit = streak 1, gift ready", dg.CheckIn() == 1 && DailyGift.Ready && CelebrationCard.History.Last().Contains("Daily gift"));
        money = eco.Money;
        T.Check("Collect day 1 gift (+50)", dg.Claim() != "" && eco.Money == money + 50 && !DailyGift.Ready);
        T.Check("Same day: no second gift", dg.CheckIn() == 1 && !DailyGift.Ready && dg.Claim() == "" && eco.Money == money + 50);
        DailyGift.Clock = () => d0.AddDays(1);
        money = eco.Money;
        T.Check("Next day: streak 2 (+80)", dg.CheckIn() == 2 && DailyGift.Ready && dg.Claim() != "" && eco.Money == money + 80);
        DailyGift.Clock = () => d0.AddDays(3);
        T.Check("Missed a day: the ladder starts again (no penalty)", dg.CheckIn() == 1 && DailyGift.Step == 0);
        int lantern = dec.Stock("fl_gold_lantern");
        for (int i = 4; i <= 9; i++) { var di = d0.AddDays(i); DailyGift.Clock = () => di; dg.CheckIn(); dg.Claim(); }
        T.Check("7 days in a row: golden lantern on day 7", DailyGift.Streak == 7 && dec.Stock("fl_gold_lantern") == lantern + 1, DailyGift.Streak + "");
        for (int i = 10; i <= 16; i++) { var di = d0.AddDays(i); DailyGift.Clock = () => di; dg.CheckIn(); dg.Claim(); }
        T.Check("The lantern is given once (second week: coins)", DailyGift.Streak == 14 && dec.Stock("fl_gold_lantern") == lantern + 1 && string.IsNullOrEmpty(DailyGift.Reward(6).decor));
        var dd = d0.AddDays(17); DailyGift.Clock = () => dd; dg.CheckIn();
        T.Check("Gift waiting on the journal overview", DailyGift.Ready);

        // ---- journal + pause menu (keyboard and gamepad)
        var prevKb = Keyboard.current; var prevPad = Gamepad.current;
        var kb = new Keyboard();
        foreach (var f in typeof(Keyboard).GetFields()) if (f.FieldType == typeof(KeyControl)) f.SetValue(kb, new KeyControl());
        Keyboard.current = kb;
        var pad = new Gamepad(); Gamepad.current = pad;
        // the opening story card of this farm may still hold the UI: close it like a player would (Enter)
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null && UIManager.Instance.IsUIOpened) typeof(StoryIntro).GetMethod("Close", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Tick(0.02f);
        T.Check("Gameplay input free before the journal checks", !UIManager.Instance.IsUIOpened);
        var js = FarmLifeScreen.Instance;
        PressKey(kb.yKey);
        T.Check("Y opens the Farm life journal (Overview)", js.IsOpen && js.Current == FarmLifeScreen.Tab.Overview && UIManager.Instance.IsUIOpened,
            "free " + CozyKeys.Free + " ui " + UIManager.Instance.IsUIOpened + " ts " + Time.timeScale + " typing " + NetSession.Typing + " menu " + GameMenu.IsOpen);
        int giftRow = js.List.Rows.ToList().FindIndex(r => r.label().Contains("Collect daily gift"));
        money = eco.Money;
        js.List.Choose(giftRow);
        T.Check("Collect the daily gift from the journal", !DailyGift.Ready && eco.Money > money);
        PressPad(pad.rightShoulder);
        T.Check("RB: next tab (Roadside stand)", js.Current == FarmLifeScreen.Tab.Orders && js.List.Rows.Any(r => r.label().Contains("Send away") || r.label().Contains("Next visitor")));
        PressPad(pad.rightShoulder); PressPad(pad.rightShoulder);
        T.Check("Calendar tab lists upcoming festivals and birthdays", js.Current == FarmLifeScreen.Tab.Calendar && js.List.Rows.Any(r => r.label().Contains("Birthday")));
        PressPad(pad.leftShoulder);
        T.Check("LB: previous tab (Friends)", js.Current == FarmLifeScreen.Tab.Friends && js.List.Rows.Any(r => r.label().Contains("Grandma Moudhi")));
        foreach (FarmLifeScreen.Tab t in Enum.GetValues(typeof(FarmLifeScreen.Tab))) js.SetTab(t);
        T.Check("Mastery and unlock tabs", js.Rows(FarmLifeScreen.Tab.Mastery).Any(r => r.label().Contains("Strawberry")) && js.Rows(FarmLifeScreen.Tab.Unlocks).Any(r => r.label().Contains("Pigeon tower")));
        PressPad(pad.buttonEast);
        T.Check("B closes the journal", !js.IsOpen && !UIManager.Instance.IsUIOpened);
        FakeEngine.Tick(0.02f);
        PressPad(pad.dpad.down);
        T.Check("D-pad down opens it too", js.IsOpen);
        PressKey(kb.yKey);
        T.Check("Y closes it", !js.IsOpen);
        var menu = GameMenu.Instance;
        menu.Open(GameMenu.Mode.Pause);
        var labels = menu.List.Rows.Select(r => r.label()).ToList();
        T.Check("Pause menu has Farm life journal and Photo mode", labels.Contains(UiText.T("Farm life journal")) && labels.Contains(UiText.T("Photo mode")));
        menu.List.Choose(labels.IndexOf(UiText.T("Farm life journal")));
        T.Check("Pause menu opens the journal and resumes", js.IsOpen && !GameMenu.IsOpen && Time.timeScale == 1f);
        js.Close();

        // ---- photo mode
        var pm = PhotoMode.Instance;
        FakeEngine.Tick(0.02f);
        var mainCam = Camera.main;
        var hudCanvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.enabled && c.isRootCanvas).ToList();
        PressKey(kb.f9Key);
        T.Check("F9: photo mode (game frozen, free camera, other cameras off, HUD hidden)", pm.Active && Time.timeScale == 0f && pm.PhotoCamera != null && pm.PhotoCamera.enabled
            && (mainCam == null || !mainCam.enabled) && hudCanvas.All(c => !c.enabled) && UIManager.Instance.IsUIOpened);
        T.Check("Help line shows the keys", (pm.HintText.Contains("F12") || pm.HintText.Contains("Y: take photo")) && pm.HelpVisible, pm.HintText);
        Vector3 p0 = pm.PhotoCamera.transform.position;
        pm.Fly(new Vector2(0, 1), Vector2.zero, 0f, 1f);
        T.Check("Free camera flies (8 m/s)", Math.Abs(Vector3.Distance(p0, pm.PhotoCamera.transform.position) - 8f) < 0.5f, Vector3.Distance(p0, pm.PhotoCamera.transform.position) + "");
        for (int i = 0; i < 20; i++) pm.Fly(new Vector2(0, 1), Vector2.zero, 0f, 1f);
        T.Check("Camera stays within 60 m", Vector3.Distance(p0, pm.PhotoCamera.transform.position) <= 60.5f);
        pm.Fly(Vector2.zero, Vector2.zero, -1f, 100f);
        T.Check("Camera stays above the ground", pm.PhotoCamera.transform.position.y >= Build.GroundY(pm.PhotoCamera.transform.position) + 0.29f);
        PressPad(pad.rightShoulder);
        T.Check("RB: next filter (warm desert)", pm.Filter == 1 && PhotoMode.FilterName(pm.Filter) == "Warm desert");
        PressPad(pad.leftShoulder); PressPad(pad.leftShoulder);
        T.Check("LB wraps to the last filter (old photo)", pm.Filter == PhotoMode.FilterNames.Length - 1);
        pm.SetFilter(3);
        PressKey(kb.f12Key);
        T.Check("F12 saves a photo to Photos/*.png", pm.LastPhoto != null && pm.LastPhoto.EndsWith(".png") && pm.LastPhoto.Contains("Photos") && ScreenCapture.LastPath == pm.LastPhoto);
        PressPad(pad.buttonWest);
        T.Check("X hides the help", !pm.HelpVisible);
        PressPad(pad.buttonEast);
        FakeEngine.Tick(0.02f);
        T.Check("B leaves: time, cameras, HUD and input restored", !pm.Active && Time.timeScale == 1f && (mainCam == null || mainCam.enabled) && hudCanvas.All(c => c.enabled) && !UIManager.Instance.IsUIOpened
            && GameObject.Find("Photo Camera") == null);
        Keyboard.current = prevKb; Gamepad.current = prevPad;

        // ---- Arabic
        Loc.SetLanguage(Language.Arabic);
        var lines = new List<string>();
        foreach (FarmLifeScreen.Tab t in Enum.GetValues(typeof(FarmLifeScreen.Tab))) foreach (var r in js.Rows(t)) lines.Add(r.label() + " " + (r.value != null ? r.value() : ""));
        lines.AddRange(fr.Lines()); lines.AddRange(cm.Lines()); lines.AddRange(Festivals.Upcoming(40));
        lines.Add(fb.Summary());
        foreach (var t in new[] { typeof(FarmLevel), typeof(RoadsideStand), typeof(Friendship), typeof(Festivals) })
            lines.Add((string)t.GetMethod("HudLine", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null) ?? "");
        lines.Add(Festivals.Instance.LastResult);
        foreach (var id in new[] { "vet", "elder", "youngfarmer" }) { lines.Add(fr.Npcs[id].GetComponent<CozyProp>().InteractionPrompt); }
        WorldClock.Instance.NextDay();
        lines.Add(fr.Talk("youngfarmer"));
        lines.Add(st.Stand.GetComponent<CozyProp>().InteractionPrompt);
        foreach (var e in ShopCatalog.Entries.Where(e => e.category == "Farm decor")) lines.Add(e.name() + " " + e.detail());
        var english = lines.Where(l => !NoEnglish(Strip(l))).ToList();
        T.Check("Farm life texts are Arabic in Arabic mode", english.Count == 0, string.Join(" || ", english.Take(3)));
        Loc.SetLanguage(Language.English);

        T.Check("No errors in the farm life features", FakeEngine.Errors.Count == errs0, string.Join(" | ", FakeEngine.Errors.Skip(errs0).Take(3)));
        levelBefore = lvl.Level; xpBefore = lvl.Xp; heartsVet = fr.Hearts("vet"); filledBefore = st.Filled; ribbonsBefore = fe.Ribbons;
        DailyGift.Clock = () => DateTime.Now;
        FarmLife.PassiveBonuses = false;
    }

    // rich text tags, key glyph tokens and number formats are not words
    static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]*>|\\[[^\\]]*\\]|Enter|Esc", "");

    public static void AfterRestart()
    {
        Console.WriteLine("Farm life after restart");
        FakeEngine.Run(2.5f);
        var lvl = FarmLevel.Instance;
        T.Check("Farm level and XP restored", lvl.Level >= levelBefore && lvl.Xp >= xpBefore - 0.5f && lvl.Xp < xpBefore + 500f, lvl.Level + " " + lvl.Xp + " vs " + levelBefore + " " + xpBefore);
        T.Check("Crop mastery restored", CropMastery.Instance.Stars("strawberry") == strawberryStars && strawberryStars == 3);
        T.Check("Friendship restored (Dr. Huda's hearts, events seen)", Friendship.Instance.Hearts("vet") == heartsVet && Friendship.Instance.EventSeen("vet", 2));
        T.Check("Roadside stand restored (orders served)", RoadsideStand.Instance.Filled == filledBefore && RoadsideStand.Instance.Stand != null);
        T.Check("Festival ribbons restored", Festivals.Instance.Ribbons == ribbonsBefore);
        T.Check("Prize decor kept in storage", Decorations.Instance.Stock("fl_crescent") >= 1);
        T.Check("New villagers back in the world", Friendship.Instance.Npcs.ContainsKey("vet") && Friendship.Instance.Npcs["vet"] != null);
    }

    public static void AfterNewGame()
    {
        Console.WriteLine("Farm life after new game");
        FakeEngine.Run(1f);
        T.Check("New game resets the farm level, XP and rewards", FarmLevel.Instance.Level == 1 && FarmLevel.Instance.Xp < 50f && FarmLevel.Instance.Pending.Count == 0, FarmLevel.Instance.Level + " " + FarmLevel.Instance.Xp);
        T.Check("New game resets mastery, friendships, stand and festivals", CropMastery.Instance.Stars("strawberry") == 0 && Friendship.Instance.Hearts("vet") == 0
            && RoadsideStand.Instance.Filled == 0 && Festivals.Instance.Ribbons == 0 && RoadsideStand.SlotCount == 1);
        T.Check("Daily gift streak is account-wide (kept)", DailyGift.Streak >= 1);
        DailyGift.ResetStreak();
    }
}
