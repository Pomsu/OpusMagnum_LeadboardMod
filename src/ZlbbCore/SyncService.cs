using System;
using System.Collections.Generic;
using System.IO;

namespace ZlbbCore
{
	public enum SyncState
	{
		NotStarted,
		Running,
		/// <summary>Index is up to date as of the last successful sync.</summary>
		Fresh,
		/// <summary>The sync failed but an older snapshot is in use.</summary>
		Stale,
		/// <summary>The sync failed and there is no data at all (e.g. first launch while offline).</summary>
		Failed,
	}

	public sealed class SyncOutcome
	{
		public SyncState State;
		public bool Bootstrap;
		public int Changes;
		public string Error;
		public TimeSpan? RetryAfter;
		/// <summary>Set when the call did nothing (already running, or manual cooldown not over).</summary>
		public string Skipped;
	}

	/// <summary>
	/// Keeps a local snapshot of all zlbb records up to date with the least possible traffic:
	/// one full download on the first run, afterwards a single small "changes since last sync" request per game start.
	/// Nothing here runs by itself - no timers, no polling. <see cref="RunOnce"/> blocks, so call it from a background thread.
	/// </summary>
	public sealed class SyncService
	{
		public static readonly DateTime BootstrapSince = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		/// <summary>Re-requested window before the last sync; harmless because ADD/REMOVE are idempotent.</summary>
		public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(10);
		public static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(60);
		public const int BootstrapTimeoutMs = 60000;
		public const int DiffTimeoutMs = 15000;

		private readonly ZlbbClient client;
		private readonly string snapshotPath;
		private readonly Func<DateTime> utcNow;
		private readonly Action<string> log;
		private readonly object runLock = new object();
		private bool running;
		private DateTime? lastManualAttemptUtc;

		private volatile RecordIndex index = new RecordIndex();
		private volatile SyncState state = SyncState.NotStarted;
		private DateTime? lastSyncUtc;

		/// <summary>Raised on the sync thread after the index changed.</summary>
		public event Action Updated;

		/// <summary>Use the server's HTTP Date as the sync time (protects against a wrong PC clock). Tests with a fake clock leave this off.</summary>
		public bool PreferServerClock;

		public SyncService(ZlbbClient client, string snapshotPath, Func<DateTime> utcNow = null, Action<string> log = null)
		{
			this.client = client;
			this.snapshotPath = snapshotPath;
			this.utcNow = utcNow ?? (() => DateTime.UtcNow);
			this.log = log ?? (s => { });
		}

		public RecordIndex Index
		{
			get { return index; }
		}

		public SyncState State
		{
			get { return state; }
		}

		public DateTime? LastSyncUtc
		{
			get { lock (runLock) return lastSyncUtc; }
		}

		public SyncOutcome RunOnce()
		{
			return Run(false);
		}

		/// <summary>User-triggered sync ("Sync now" button); refused while another sync runs or within the cooldown.</summary>
		public SyncOutcome SyncNow()
		{
			return Run(true);
		}

		private SyncOutcome Run(bool manual)
		{
			lock (runLock)
			{
				if (running)
					return new SyncOutcome { State = state, Skipped = "a sync is already running" };
				if (manual && lastManualAttemptUtc.HasValue && utcNow() - lastManualAttemptUtc.Value < ManualCooldown)
				{
					int wait = (int)Math.Ceiling((ManualCooldown - (utcNow() - lastManualAttemptUtc.Value)).TotalSeconds);
					return new SyncOutcome { State = state, Skipped = "please wait " + wait + " s before syncing again" };
				}
				if (manual)
					lastManualAttemptUtc = utcNow();
				running = true;
			}
			try
			{
				return Sync();
			}
			catch (Exception e) when (!(e is OutOfMemoryException))
			{
				// anything unexpected: report it instead of leaving the state at "Running" for good
				state = lastSyncUtc.HasValue ? SyncState.Stale : SyncState.Failed;
				log("sync failed unexpectedly: " + e.Message);
				return new SyncOutcome { State = state, Error = e.Message };
			}
			finally
			{
				lock (runLock)
					running = false;
			}
		}

		private SyncOutcome Sync()
		{
			state = SyncState.Running;
			var outcome = new SyncOutcome();

			bool haveData = lastSyncUtc.HasValue;
			if (!haveData)
			{
				Snapshot snapshot = SnapshotStore.Load(snapshotPath);
				if (snapshot != null && snapshot.LastSyncUtc > utcNow().AddDays(1))
				{
					log("snapshot has a sync time in the future; ignoring it");
					snapshot = null;
				}
				if (snapshot != null)
				{
					var loaded = new RecordIndex();
					foreach (ZlbbRecord record in snapshot.Records)
						loaded.Apply(new RecordChange { Type = ChangeType.Add, Record = record });
					index = loaded;
					lock (runLock)
						lastSyncUtc = snapshot.LastSyncUtc;
					haveData = true;
					log("loaded snapshot: " + loaded.Count + " records, last sync " + snapshot.LastSyncUtc.ToString("u"));
					RaiseUpdated();
				}
			}

			DateTime since = haveData ? lastSyncUtc.Value - Overlap : BootstrapSince;
			bool bootstrap = !haveData;
			outcome.Bootstrap = bootstrap;
			DateTime requestStart = utcNow();
			var changes = new List<RecordChange>();
			FetchStats fetched;
			try
			{
				fetched = client.FetchChanges(since, changes.Add, bootstrap ? BootstrapTimeoutMs : DiffTimeoutMs);
			}
			catch (Exception e) when (!(e is OutOfMemoryException))
			{
				outcome.State = haveData ? SyncState.Stale : SyncState.Failed;
				outcome.Error = e.Message;
				var http = e as ZlbbHttpException;
				if (http != null)
					outcome.RetryAfter = http.RetryAfter;
				state = outcome.State;
				log("sync failed (" + (bootstrap ? "full download" : "diff") + "): " + e.Message + (haveData ? " - using the older snapshot" : ""));
				return outcome;
			}

			RecordIndex target = bootstrap ? new RecordIndex() : index;
			target.ApplyAll(changes);
			index = target;
			outcome.Changes = changes.Count;

			// The moment "up to date as of": the server's clock when we may trust it (a PC clock that runs fast would otherwise make later
			// syncs skip changes), else the local time from just before the request. The overlap window covers small differences.
			DateTime syncTime = PreferServerClock && fetched.ServerTimeUtc.HasValue ? fetched.ServerTimeUtc.Value : requestStart;
			if (PreferServerClock && fetched.ServerTimeUtc.HasValue && Math.Abs((fetched.ServerTimeUtc.Value - requestStart).TotalMinutes) > Overlap.TotalMinutes)
				log("this PC's clock differs from zlbb's by " + (int)(requestStart - fetched.ServerTimeUtc.Value).TotalMinutes + " minutes; using zlbb's time for syncing");

			bool saved = true;
			try
			{
				SnapshotStore.Save(snapshotPath, new Snapshot { LastSyncUtc = syncTime, Records = target.GetAllRecords() });
			}
			catch (Exception e) when (!(e is OutOfMemoryException))
			{
				saved = false;
				log("could not save snapshot: " + e.Message);
			}
			if (saved)
				lock (runLock)
					lastSyncUtc = syncTime;
			else if (!haveData)
				lock (runLock)
					lastSyncUtc = null; // nothing persisted: the next launch downloads again

			outcome.State = SyncState.Fresh;
			state = SyncState.Fresh;
			log((bootstrap ? "full download" : "diff") + " applied: " + changes.Count + " changes, " + target.Count + " records");
			RaiseUpdated();
			return outcome;
		}

		private void RaiseUpdated()
		{
			Action handler = Updated;
			if (handler != null)
			{
				try
				{
					handler();
				}
				catch (Exception e)
				{
					log("Updated handler failed: " + e.Message);
				}
			}
		}
	}
}
