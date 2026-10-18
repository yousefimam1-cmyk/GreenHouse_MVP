using UnityEngine;

/// <summary>What the farm looks like this hour (gathered from the scene by UtilityGrid, or set directly in tests).</summary>
public class UtilityEnv
{
    public float sun = 1f;                 // 0..1 sun height
    public Weather weather = Weather.Sunny;
    public Season season = Season.Spring;
    public int day = 1;
    public int hour = 12;                  // 0..23 clock hour
    public int greenhouses;
    public int beds;
    /// <summary>kW of the FarmCore irrigation pumps on grid power (UtilityHooks.IrrigationKw).</summary>
    public float irrigationKw;
    public float animalWater;              // m3/day
    public float manureKg;                 // manure in storage
    public bool wellRunning = true;
}

/// <summary>
/// Power (kW) and water (m3/day) supply vs demand for the farm. Pure logic, no scene access, so it can be
/// tested headless. Sources are bought in the shop; Compute() works out this hour's rates and the factors
/// fed to UtilityHooks; Advance() also books the hour (fuel, manure, stored water, bills, daily water use).
/// Game-balance numbers loosely based on Al-Qassim: SEC agricultural tariff, Saudi diesel price, Saq aquifer wells.
/// </summary>
public class UtilityModel
{
    // ---- prices / constants (SAR) ----
    public const float GridTariff = 0.18f;         // SAR per kWh from SEC
    public const float NetBillingCredit = 0.07f;   // SAR per surplus renewable kWh exported
    public const float DieselPrice = 1.66f;        // SAR per litre
    public const float DieselPerKwh = 0.3f;        // litres per kWh
    public const float TankerPrice = 12f;          // SAR per m3 desalinated water
    public const float TsePrice = 0.5f;            // SAR per m3 treated wastewater
    public const float KwhPerKgManure = 2f;
    public const float DailyWaterLimit = 45f;      // m3/day conservation allowance
    public const float BaseReservoir = 20f, TankSize = 50f, DieselTankL = 800f;
    public const float SolarKwp = 10f, WindKw = 5f, BiogasKw = 8f, GeneratorKw = 30f;
    public const float WellM3 = 25f, DeepWellM3 = 60f, DeepPumpKw = 15f, TseM3 = 30f;
    public const int MaxSolar = 4, MaxWind = 2, MaxTanks = 3;
    public static readonly float[] GridKw = { 20f, 50f, 100f };
    public static readonly float[] GridDailyFee = { 3f, 8f, 15f };
    public static readonly int[] GridUpgradeCost = { 4000, 9000 };

    // ---- owned equipment ----
    public int GridLevel;
    public int Solar, Wind, Tanks;
    public bool Generator, Biogas, DeepWell, Tse;

    // ---- stocks ----
    public float DieselL;
    public float Dust;             // 0..0.6 on the solar panels
    public float Stored = BaseReservoir;

    // ---- today ----
    public float GridKwhToday, ExportKwhToday, DieselLToday, WaterUsedToday, TseUsedToday;
    public bool PowerShortToday, WaterShortToday, WarnedLimit80, WarnedLimit;
    public int PowerOkDays;

    // ---- this hour (rates) ----
    public float CoolingKw, PumpKw, IrrigationKw, BaseKw, PowerDemand, PowerServed;
    public float SolarOut, WindOut, BiogasOut, GridOut, DieselOut;
    public float GhWater, FieldWater, AnimalWater, WaterDemand;
    public float WellOut, DeepOut, TseOut, WaterSupply;
    public float PowerRatio = 1f, WaterRatio = 1f;
    public float GreenhouseFactor = 1f;

    public float StorageCapacity => BaseReservoir + Tanks * TankSize;
    public float PowerCapacity => SolarKwp * Solar + WindKw * Wind + (Biogas ? BiogasKw : 0f) + GridKw[Mathf.Clamp(GridLevel, 0, GridKw.Length - 1)] + (Generator ? GeneratorKw : 0f);
    public bool PowerShort => PowerDemand > 0.01f && PowerRatio < 0.995f;
    public bool WaterShort => WaterDemand > 0.01f && WaterRatio < 0.995f;
    public int NextGridCost => GridLevel < GridUpgradeCost.Length ? GridUpgradeCost[GridLevel] : -1;

    public void ResetAll()
    {
        GridLevel = 0; Solar = Wind = Tanks = 0; Generator = Biogas = DeepWell = Tse = false;
        DieselL = 0f; Dust = 0f; Stored = BaseReservoir;
        GridKwhToday = ExportKwhToday = DieselLToday = WaterUsedToday = TseUsedToday = 0f;
        PowerShortToday = WaterShortToday = WarnedLimit80 = WarnedLimit = false; PowerOkDays = 0;
        PowerRatio = WaterRatio = GreenhouseFactor = 1f;
    }

    // ---------------- climate helpers ----------------

    public static float CoolingSeason(Season s) { switch (s) { case Season.Summer: return 1.8f; case Season.Autumn: return 1.2f; case Season.Winter: return 0.5f; default: return 1.1f; } }
    public static float CoolingWeather(Weather w) { switch (w) { case Weather.Heatwave: return 1.4f; case Weather.Cloudy: return 0.85f; case Weather.Rain: return 0.7f; default: return 1f; } }
    public static float WaterSeason(Season s) { switch (s) { case Season.Summer: return 1.5f; case Season.Autumn: return 1.1f; case Season.Winter: return 0.7f; default: return 1f; } }
    public static float SolarWeather(Weather w) { switch (w) { case Weather.Cloudy: return 0.5f; case Weather.Rain: return 0.25f; case Weather.Heatwave: return 0.9f; default: return 1f; } }

    /// <summary>Deterministic gusty wind 0..1 from day and hour (stronger in the afternoon and in unsettled weather).</summary>
    public static float WindFactor(int day, int hour, Weather w)
    {
        float n = Mathf.Abs(Mathf.Sin(day * 12.9898f + hour * 78.233f) * 43758.5453f);
        n -= Mathf.Floor(n);
        float f = 0.2f + 0.5f * n + (hour >= 12 && hour <= 18 ? 0.15f : 0f) + (w == Weather.Cloudy || w == Weather.Rain ? 0.15f : 0f);
        return Mathf.Clamp01(f);
    }

    public float GreenhouseCoolingKw(UtilityEnv e)
        => e.greenhouses * (5f * CoolingSeason(e.season) * CoolingWeather(e.weather) * (0.55f + 0.45f * Mathf.Clamp01(e.sun)) + 0.8f);

    /// <summary>Work out this hour's rates. With hours &gt; 0 the hour is also booked. Returns kg of manure the digester used.</summary>
    public float Advance(UtilityEnv e, float hours)
    {
        bool commit = hours > 0f;
        float h = commit ? hours : 1f;
        float sun = Mathf.Clamp01(e.sun);

        // ---- water demand (m3/day) ----
        float ws = WaterSeason(e.season) * (e.weather == Weather.Heatwave ? 1.3f : 1f);
        GhWater = 0.6f * e.beds * ws;
        FieldWater = 0f;   // the fields have their own irrigation network (FarmCore IrrigationSystem: wells, canal, tanks)
        AnimalWater = Mathf.Max(0f, e.animalWater);
        WaterDemand = GhWater + FieldWater + AnimalWater;
        TseOut = 0f;   // treated wastewater only goes to the fields: the irrigation network takes it (IrrigationSystem, tseM3h)
        WellOut = e.wellRunning ? WellM3 : 0f;
        float freshDemand = WaterDemand - TseOut;
        float space = StorageCapacity - Stored;
        float pumpDuty = DeepWell ? Mathf.Clamp01((freshDemand - WellOut + Mathf.Min(space, 24f)) / DeepWellM3) : 0f;

        // ---- power demand (kW) ----
        CoolingKw = GreenhouseCoolingKw(e);
        PumpKw = DeepPumpKw * pumpDuty;
        IrrigationKw = Mathf.Max(0f, e.irrigationKw);
        BaseKw = 2f;
        PowerDemand = CoolingKw + PumpKw + IrrigationKw + BaseKw;

        // ---- power supply, cheapest first: solar, wind, biogas, SEC grid, diesel ----
        float need = PowerDemand;
        float solarAvail = SolarKwp * Solar * sun * SolarWeather(e.weather) * (1f - Dust);
        float windAvail = WindKw * Wind * WindFactor(e.day, e.hour, e.weather);
        SolarOut = Mathf.Min(solarAvail, need); need -= SolarOut;
        WindOut = Mathf.Min(windAvail, need); need -= WindOut;
        float surplus = solarAvail + windAvail - SolarOut - WindOut;
        BiogasOut = Biogas ? Mathf.Min(Mathf.Min(BiogasKw, need), Mathf.Max(0f, e.manureKg) * KwhPerKgManure / h) : 0f; need -= BiogasOut;
        GridOut = Mathf.Min(GridKw[Mathf.Clamp(GridLevel, 0, GridKw.Length - 1)], need); need -= GridOut;
        DieselOut = Generator ? Mathf.Min(Mathf.Min(GeneratorKw, need), DieselL / DieselPerKwh / h) : 0f; need -= DieselOut;
        PowerServed = PowerDemand - Mathf.Max(0f, need);
        PowerRatio = PowerDemand > 0.01f ? Mathf.Clamp01(PowerServed / PowerDemand) : 1f;

        // ---- water supply (deep well pump only delivers what it gets power for) ----
        DeepOut = DeepWellM3 * pumpDuty * PowerRatio;
        WaterSupply = WellOut + DeepOut + TseOut;
        float hourDemand = freshDemand * h / 24f, hourSupply = (WellOut + DeepOut) * h / 24f;
        float stored = Stored, shortfall = 0f;
        if (hourSupply >= hourDemand) stored = Mathf.Min(StorageCapacity, stored + hourSupply - hourDemand);
        else
        {
            float gap = hourDemand - hourSupply;
            float draw = Mathf.Min(stored, gap);
            stored -= draw; shortfall = gap - draw;
        }
        float total = WaterDemand * h / 24f;
        WaterRatio = total > 1e-4f ? Mathf.Clamp01((total - shortfall) / total) : 1f;

        // ---- effects on crops ----
        float gh = Mathf.Min(PowerRatio, WaterRatio);
        GreenhouseFactor = gh < 0.2f ? 0f : gh;

        float manureUsed = BiogasOut * h / KwhPerKgManure;
        if (!commit) return 0f;

        Stored = stored;
        GridKwhToday += GridOut * h;
        ExportKwhToday += surplus * h;
        float litres = DieselOut * h * DieselPerKwh;
        DieselL = Mathf.Max(0f, DieselL - litres);
        DieselLToday += litres;
        WaterUsedToday += (freshDemand * h / 24f) - shortfall;
        TseUsedToday += TseOut * h / 24f;
        if (PowerShort) PowerShortToday = true;
        if (WaterShort) WaterShortToday = true;
        return manureUsed;
    }

    /// <summary>SEC bill for the day so far (fixed fee + energy - net-billing credit, never below the fee) plus treated wastewater.</summary>
    public float BillSoFar()
    {
        float energy = Mathf.Max(0f, GridKwhToday * GridTariff - ExportKwhToday * NetBillingCredit);
        return GridDailyFee[Mathf.Clamp(GridLevel, 0, GridDailyFee.Length - 1)] + energy + TseUsedToday * TsePrice;
    }

    /// <summary>Close the day: returns the bill to pay, updates the power streak, adds dust to the panels and clears today's counters.</summary>
    public float EndDay(Season season, Weather weather)
    {
        float bill = BillSoFar();
        PowerOkDays = PowerShortToday ? 0 : PowerOkDays + 1;
        Dust = Mathf.Min(0.6f, Dust + 0.06f + (season == Season.Summer ? 0.04f : 0f) + (weather == Weather.Heatwave ? 0.05f : 0f));
        GridKwhToday = ExportKwhToday = DieselLToday = WaterUsedToday = TseUsedToday = 0f;
        PowerShortToday = WaterShortToday = WarnedLimit80 = WarnedLimit = false;
        return bill;
    }

    /// <summary>Estimated running cost for a full day at this hour's rates (SAR).</summary>
    public float DailyCostEstimate()
    {
        float energy = Mathf.Max(0f, GridOut * 24f * GridTariff);
        return GridDailyFee[Mathf.Clamp(GridLevel, 0, GridDailyFee.Length - 1)] + energy + DieselOut * 24f * DieselPerKwh * DieselPrice + TseOut * TsePrice;
    }
}
