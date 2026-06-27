using ChargeManager.Services;

namespace ChargeManager.Tests.ImportTracking;

public class GridAttributionCalculatorTests
{
	private static readonly DateTime T0 = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	private static EnergyRecord Rec(int startSec, int endSec, double wh) =>
		new(T0.AddSeconds(startSec), T0.AddSeconds(endSec), wh);

	// ── TryFindRelevantRecords ──────────────────────────────────────────────

	[Test]
	public async Task TryFindRelevantRecords_EmptyHistory_ReturnsFalse()
	{
		var found = GridAttributionCalculator.TryFindRelevantRecords([], Rec(0, 30, 10), out _);
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async Task TryFindRelevantRecords_SingleRecordExactMatch_ReturnsTrue()
	{
		List<EnergyRecord> grid = [Rec(0, 30, 10)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(0, 30, 5), out var records);
		await Assert.That(found).IsTrue();
		await Assert.That(records.Count()).IsEqualTo(1);
	}

	[Test]
	public async Task TryFindRelevantRecords_SingleRecordContainsEvPeriod_ReturnsTrue()
	{
		// Grid record [0,60] fully contains EV period [10,50]
		List<EnergyRecord> grid = [Rec(0, 60, 10)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(10, 50, 5), out _);
		await Assert.That(found).IsTrue();
	}

	[Test]
	public async Task TryFindRelevantRecords_EvBeforeAllGridRecords_ReturnsFalse()
	{
		List<EnergyRecord> grid = [Rec(60, 120, 10)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(0, 30, 5), out _);
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async Task TryFindRelevantRecords_EvAfterAllGridRecords_ReturnsFalse()
	{
		List<EnergyRecord> grid = [Rec(0, 30, 10)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(60, 90, 5), out _);
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async Task TryFindRelevantRecords_EvStartHasNoLeftBookend_ReturnsFalse()
	{
		// EV starts at t=0 but first grid record starts at t=10 → no left bookend
		List<EnergyRecord> grid = [Rec(10, 40, 10)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(0, 40, 5), out _);
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async Task TryFindRelevantRecords_EvEndHasNoRightBookend_ReturnsFalse()
	{
		// EV ends at t=40 but last grid record ends at t=30 → no right bookend
		List<EnergyRecord> grid = [Rec(0, 30, 10)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(0, 40, 5), out _);
		await Assert.That(found).IsFalse();
	}

	[Test]
	public async Task TryFindRelevantRecords_MultipleRecordsBookendEvPeriod_ReturnsContiguousSlice()
	{
		// EV spans [5,25]; three 10s grid records [0,10],[10,20],[20,30] together bookend it
		List<EnergyRecord> grid = [Rec(0, 10, 5), Rec(10, 20, 5), Rec(20, 30, 5)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(5, 25, 8), out var records);
		await Assert.That(found).IsTrue();
		await Assert.That(records.Count()).IsEqualTo(3);
	}

	[Test]
	public async Task TryFindRelevantRecords_OutOfOrderGridHistory_SortsBeforeMatching()
	{
		// Records inserted in reverse order; should still find correct bookends
		List<EnergyRecord> grid = [Rec(20, 30, 5), Rec(0, 10, 5), Rec(10, 20, 5)];
		var found = GridAttributionCalculator.TryFindRelevantRecords(grid, Rec(5, 25, 8), out var records);
		await Assert.That(found).IsTrue();
		await Assert.That(records.Count()).IsEqualTo(3);
	}

	// ── CalculateGridEnergyForEv ────────────────────────────────────────────

	[Test]
	public async Task CalculateGridEnergyForEv_ZeroEvEnergy_ReturnsZero()
	{
		List<EnergyRecord> grid = [Rec(0, 30, 100)];
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 30, 0), grid);
		await Assert.That(gridEnergyForEv).IsEqualTo(0);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_NegativeEvEnergy_ReturnsZero()
	{
		List<EnergyRecord> grid = [Rec(0, 30, 100)];
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 30, -5), grid);
		await Assert.That(gridEnergyForEv).IsEqualTo(0);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_GridImportExceedsEvConsumption_CapsAtEvConsumption()
	{
		// EV used 100 Wh, grid imported 200 Wh → attribution capped at 100
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 30, 100), [Rec(0, 30, 200)]);
		await Assert.That(gridEnergyForEv).IsEqualTo(100);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_GridImportLessThanEvConsumption_ReturnsGridImport()
	{
		// EV used 100 Wh but grid only imported 50 Wh → only 50 Wh attributable to grid
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 30, 100), [Rec(0, 30, 50)]);
		await Assert.That(gridEnergyForEv).IsEqualTo(50);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_ZeroGridImport_ReturnsZero()
	{
		// Grid imported nothing → EV ran entirely on solar
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 30, 100), [Rec(0, 30, 0)]);
		await Assert.That(gridEnergyForEv).IsEqualTo(0);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_PartialTimeOverlap_AttributesProportionally()
	{
		// Grid record spans [0,60] and imported 60 Wh. EV only covers the second half [30,60].
		// Proportional overlap = 30s/60s = 50% → 30 Wh attributed. EV consumed 20 Wh → cap at 20.
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(30, 60, 20), [Rec(0, 60, 60)]);
		await Assert.That(gridEnergyForEv).IsEqualTo(20);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_PartialTimeOverlap_ProportionalExceedsEv_CapsAtEv()
	{
		// Grid record spans [0,60] and imported 120 Wh. EV covers second half [30,60].
		// Proportional overlap = 50% → 60 Wh attributed. EV consumed 40 Wh → cap at 40.
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(30, 60, 40), [Rec(0, 60, 120)]);
		await Assert.That(gridEnergyForEv).IsEqualTo(40);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_MultipleGridRecords_SumsProportionalOverlaps()
	{
		// Two back-to-back 30s records each importing 40 Wh. EV spans [0,60] consuming 50 Wh.
		// Full overlap on both → 40+40=80 Wh attributed, capped at 50.
		List<EnergyRecord> grid = [Rec(0, 30, 40), Rec(30, 60, 40)];
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 60, 50), grid);
		await Assert.That(gridEnergyForEv).IsEqualTo(50);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_PureSolarCharging_ReturnsZero()
	{
		// All grid records show zero import → EV ran entirely on solar
		List<EnergyRecord> grid = [Rec(0, 10, 0), Rec(10, 20, 0), Rec(20, 30, 0)];
		var (gridEnergyForEv, _) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 30, 100), grid);
		await Assert.That(gridEnergyForEv).IsEqualTo(0);
	}

	[Test]
	public async Task CalculateGridEnergyForEv_TotalGridImportIsRawSum()
	{
		// TotalGridImport should be the raw sum of WattHours across all records (not proportional)
		List<EnergyRecord> grid = [Rec(0, 30, 30), Rec(30, 60, 70)];
		var (_, totalGridImport) = GridAttributionCalculator.CalculateGridEnergyForEv(Rec(0, 60, 200), grid);
		await Assert.That(totalGridImport).IsEqualTo(100);
	}
}
