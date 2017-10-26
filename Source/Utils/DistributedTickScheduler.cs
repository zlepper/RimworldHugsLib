﻿using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace HugsLib.Utils {
	/// <summary>
	/// A ticking scheduler for things that require a tick only every so often.
	/// Distributes tick calls uniformly over multiple frames to reduce the workload.
	/// Optimized for many tick recipients with the same tick interval.
	/// </summary>
	public class DistributedTickScheduler {
		private readonly Dictionary<Thing, TickableEntry> entries = new Dictionary<Thing, TickableEntry>();
		private readonly List<ListTicker> tickers = new List<ListTicker>();
		private readonly Queue<Thing> unregisterQueue = new Queue<Thing>();
		private int lastProcessedTick = -1;

		internal DistributedTickScheduler() {
		}

		/// <summary>
		/// Registers a delegate to be called every tickInterval ticks.
		/// </summary>
		/// <param name="callback">The delegate that will be called</param>
		/// <param name="tickInterval">The interval between the calls (for example 30 to have the delegate be called 2 times a second)</param>
		/// <param name="owner">The Thing the delegate is attached to. The callback will be automatically unregistered if the owner is found to be despawned at call time.</param>
		public void RegisterTickability(Action callback, int tickInterval, Thing owner) {
			if (lastProcessedTick < 0) throw new Exception("Adding callback to not initialized DistributedTickScheduler");
			if(owner == null || owner.Destroyed) throw new Exception("A non-null, not destroyed owner Thing is required to register for tickability");
			if (tickInterval < 1) throw new Exception("Invalid tick interval: " + tickInterval);
			if (entries.ContainsKey(owner)) {
				HugsLibController.Logger.Warning("DistributedTickScheduler tickability already registered for: " + owner);
			} else {
				var entry = new TickableEntry(callback, tickInterval, owner);
				entries.Add(owner, entry);
				GetTicker(tickInterval).Register(entry);
			}
		}

		/// <summary>
		/// Manually removes a delegate to prevent further calls.
		/// </summary>
		/// <exception cref="ArgumentException">Throws if the provided owner is not registered. Use IsRegistered() to check.</exception>
		/// <param name="owner">The Thing the delegate was registered with</param>
		public void UnregisterTickability(Thing owner) {
			if (!IsRegistered(owner)) throw new ArgumentException("Cannot unregister non-registered owner: " + owner);
			var entry = entries[owner];
			GetTicker(entry.interval).Unregister(entry);
			entries.Remove(owner);
		}

		/// <summary>
		/// Returns true if the passed Thing is registered as the owner of a delegate.
		/// </summary>
		/// <param name="owner"></param>
		/// <returns></returns>
		public bool IsRegistered(Thing owner) {
			return entries.ContainsKey(owner);
		}

		/// <summary>
		/// Only for debug purposes
		/// </summary>
		public IEnumerable<TickableEntry> GetAllEntries() {
			return entries.Values;
		}

		internal void Initialize(int currentTick) {
			entries.Clear();
			tickers.Clear();
			lastProcessedTick = currentTick;
		}

		internal void Tick(int currentTick) {
			if (lastProcessedTick < 0) throw new Exception("Ticking not initialized DistributedTickScheduler");
			lastProcessedTick = currentTick;
			for (var i = 0; i < tickers.Count; i++) {
				tickers[i].Tick(currentTick);
			}
			UnregisterQueuedOwners();
		}

		private void UnregisterAtEndOfTick(Thing owner) {
			unregisterQueue.Enqueue(owner);
		}

		private void UnregisterQueuedOwners() {
			while (unregisterQueue.Count > 0) {
				var owner = unregisterQueue.Dequeue();
				if (IsRegistered(owner)) {
					UnregisterTickability(owner);
				}
			}
		}

		private ListTicker GetTicker(int interval) {
			for (int i = 0; i < tickers.Count; i++) {
				if (tickers[i].tickInterval == interval) return tickers[i];
			}
			var ticker = new ListTicker(interval, this);
			tickers.Add(ticker);
			return ticker;
		}

		public class TickableEntry {
			public readonly Action callback;
			public readonly int interval;
			public readonly Thing owner;

			public TickableEntry(Action callback, int interval, Thing owner) {
				this.callback = callback;
				this.interval = interval;
				this.owner = owner;
			}
		}

		private class ListTicker {
			public readonly int tickInterval;
			private readonly DistributedTickScheduler scheduler;
			private readonly List<TickableEntry> tickList = new List<TickableEntry>();
			private int currentIndex;
			private int nextCycleStart;

			public ListTicker(int tickInterval, DistributedTickScheduler scheduler) {
				this.tickInterval = tickInterval;
				this.scheduler = scheduler;
			}

			public void Tick(int currentTick) {
				if (nextCycleStart <= currentTick) {
					currentIndex = 0;
					nextCycleStart = currentTick + tickInterval;
				}
				var numCallbacksThisTick = Math.Ceiling(tickList.Count/(float) tickInterval);
				while (numCallbacksThisTick > 0) {
					if (currentIndex >= tickList.Count) break;
					var entry = tickList[currentIndex];
					if (entry.owner.Spawned) {
						try {
							entry.callback();
						} catch (Exception e) {
							HugsLibController.Logger.Error("DistributedTickScheduler caught an exception while calling {0} registered by {1}: {2}",
								HugsLibUtility.DescribeDelegate(entry.callback), entry.owner, e);
						}
					} else {
						scheduler.UnregisterAtEndOfTick(entry.owner);
					}
					currentIndex++;
					numCallbacksThisTick--;
				}

			}

			public void Register(TickableEntry entry) {
				tickList.Add(entry);
			}

			public void Unregister(TickableEntry entry) {
				tickList.Remove(entry);
			}
		}
	}
}