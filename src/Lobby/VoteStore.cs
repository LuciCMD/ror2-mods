using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.Shared;
using MonoMod.RuntimeDetour;
using RoR2;

namespace JustBeBetter.Lobby
{
    // The game keeps each player's lobby votes (difficulty, artifacts, expansions) only until it closes
    // Here they're also written to disk per game profile, by the choices' names so adding or removing mods
    // doesn't shift them, and read back when the profile signs in
    internal sealed class VoteStore
    {
        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ManualLogSource log;
        private const string Kind = "Votes";

        private readonly List<Hook> hooks = new List<Hook>();
        private readonly Dictionary<string, string> lastSaved = new Dictionary<string, string>();
        private IDictionary? votesCache;
        private PropertyInfo? choiceValue;
        private PropertyInfo? hasVoted;

        internal VoteStore(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            try
            {
                Type manager = typeof(PreGameRuleVoteController).GetNestedType("LocalUserBallotPersistenceManager", Any);
                Type vote = typeof(PreGameRuleVoteController).GetNestedType("Vote", Any);
                votesCache = (IDictionary)manager.GetField("votesCache", Any).GetValue(null);
                choiceValue = vote.GetProperty("choiceValue", Any);
                hasVoted = vote.GetProperty("hasVoted", Any);
                hooks.Add(new Hook(manager.GetMethod("OnLocalUserSignIn", Any), new Action<Action<LocalUser>, LocalUser>(OnSignIn)));
                hooks.Add(new Hook(manager.GetMethod("OnVotesUpdated", Any), new Action<Action>(OnVotesUpdated)));
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't set up vote saving; votes last only until the game closes: {e}");
                Disable();
            }
        }

        internal void Disable()
        {
            foreach (Hook hook in hooks)
            {
                hook.Dispose();
            }

            hooks.Clear();
        }

        // The game adds an empty entry for the profile, filled from disk if a ballot was saved
        private void OnSignIn(Action<LocalUser> orig, LocalUser localUser)
        {
            orig(localUser);
            try
            {
                Load(localUser);
            }

            // Any error, since this runs inside the game's sign-in event and would cut off its other listeners
            catch (Exception e)
            {
                log.LogWarning($"Couldn't load saved votes: {e.Message}");
            }
        }

        private void OnVotesUpdated(Action orig)
        {
            orig();
            try
            {
                SaveAll();
            }
            catch (Exception e)
            {
                log.LogWarning($"Couldn't save votes: {e.Message}");
            }
        }

        private void Load(LocalUser localUser)
        {
            string? path = ProfileFiles.PathFor(Kind, localUser);
            if (path == null || !File.Exists(path) || votesCache == null || RuleCatalog.ruleCount == 0)
            {
                return;
            }

            Array ballot = (Array)typeof(PreGameRuleVoteController).GetMethod("CreateBallot", Any).Invoke(null, null);
            int applied = 0;
            foreach (string line in File.ReadAllLines(path).Take(RuleCatalog.ruleCount))
            {
                RuleChoiceDef? choice = RuleCatalog.FindChoiceDef(line.Trim());
                if (choice?.ruleDef == null)
                {
                    continue;
                }

                object vote = ballot.GetValue(choice.ruleDef.globalIndex);
                choiceValue!.SetValue(vote, choice.localIndex);
                ballot.SetValue(vote, choice.ruleDef.globalIndex);
                applied++;
            }

            votesCache[localUser] = ballot;
            log.Verbose($"Votes: {applied} restored for {localUser.userProfile?.name}");
        }

        private void SaveAll()
        {
            if (votesCache == null)
            {
                return;
            }

            foreach (DictionaryEntry entry in votesCache)
            {
                if (entry.Key is LocalUser localUser && entry.Value is Array ballot)
                {
                    Save(localUser, ballot);
                }
            }
        }

        private void Save(LocalUser localUser, Array ballot)
        {
            string? path = ProfileFiles.PathFor(Kind, localUser);
            if (path == null)
            {
                return;
            }

            var names = new List<string>();
            int count = Math.Min(ballot.Length, RuleCatalog.ruleCount);
            for (int i = 0; i < count; i++)
            {
                object vote = ballot.GetValue(i);
                if (!(bool)hasVoted!.GetValue(vote))
                {
                    continue;
                }

                int local = (int)choiceValue!.GetValue(vote);
                RuleDef rule = RuleCatalog.GetRuleDef(i);
                if (rule != null && local >= 0 && local < rule.choices.Count)
                {
                    names.Add(rule.choices[local].globalName);
                }
            }

            string text = string.Join(Environment.NewLine, names);
            if (lastSaved.TryGetValue(path, out string? previous) && previous == text)
            {
                return;
            }

            ProfileFiles.Write(path, text);
            lastSaved[path] = text;
        }
    }
}
