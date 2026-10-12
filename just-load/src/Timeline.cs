using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace JustLoad
{
    internal readonly struct Span
    {
        internal Span(string label, long start, long end)
        {
            Label = label;
            Start = start;
            End = end;
        }

        internal string Label { get; }

        internal long Start { get; }

        internal long End { get; }

        internal double Seconds => Timeline.Seconds(End - Start);
    }

    // Everything measured during startup and stage loads, in Stopwatch ticks, which are the same in both assemblies
    internal static class Timeline
    {
        internal static readonly List<Span> Milestones = new List<Span>();
        internal static readonly List<Span> Systems = new List<Span>();
        internal static readonly List<Span> Freezes = new List<Span>();

        // Stopwatch ticks at process start, worked out from the process's start time
        internal static readonly long LaunchTicks = FindLaunchTicks();

        // The label of whatever the game said it was doing last, for naming freezes
        internal static string CurrentStep { get; private set; } = "mods starting";

        internal static double Seconds(long ticks)
        {
            return ticks / (double)Stopwatch.Frequency;
        }

        internal static double SinceLaunch(long ticks)
        {
            return Seconds(ticks - LaunchTicks);
        }

        internal static void Milestone(string label)
        {
            long now = Stopwatch.GetTimestamp();
            Milestones.Add(new Span(label, now, now));
            CurrentStep = label;
        }

        // Names freezes without adding a startup milestone
        internal static void Step(string label)
        {
            CurrentStep = label;
        }

        private static long FindLaunchTicks()
        {
            long now = Stopwatch.GetTimestamp();
            try
            {
                TimeSpan sinceStart = DateTime.Now - Process.GetCurrentProcess().StartTime;
                return now - (long)(sinceStart.TotalSeconds * Stopwatch.Frequency);
            }
            // Mono can refuse the start time; the report then counts from the plugin loading
            catch (Exception)
            {
                return now;
            }
        }
    }
}
