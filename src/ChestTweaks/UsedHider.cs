using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;

namespace JustBeBetter.ChestTweaks
{
    // Fades or hides a used interactable after a delay, and puts it all back when removed
    internal sealed class UsedHider : MonoBehaviour
    {
        // QoLChests' strength for a faded one
        private const float Faded = 0.25f;

        private readonly List<GameObject> skip = new List<GameObject>();
        private float goneAt;
        private Action? restore;
        private Action? onGone;

        internal bool Fading { get; private set; }

        internal bool Gone => restore != null;

        internal void Begin(float delay, bool fade, Action? whenGone)
        {
            Restore();
            goneAt = Time.time + Mathf.Max(0f, delay);
            Fading = fade;
            onGone = whenGone;
            enabled = true;
        }

        // A shop stand's terminals sit under it but fade and come back on their own
        internal void Skip(GameObject child)
        {
            if (child && !skip.Contains(child))
            {
                skip.Add(child);
            }
        }

        // Keeps any time left, and swaps a gone one over in the same frame so it never shows in between
        internal void Switch(bool fade)
        {
            if (Fading == fade)
            {
                return;
            }

            Fading = fade;
            if (Gone)
            {
                Restore();
                restore = Apply();
            }
        }

        internal void Restore()
        {
            Action? undo = restore;
            restore = null;
            undo?.Invoke();
        }

        private void Update()
        {
            if (Time.time < goneAt)
            {
                return;
            }

            enabled = false;
            restore = Apply();
            onGone?.Invoke();
        }

        private void OnDestroy() => Restore();

        private Action Apply()
        {
            var undo = new List<Action>();
            foreach (Renderer renderer in Renderers())
            {
                if (!renderer || !renderer.enabled)
                {
                    continue;
                }

                // The model stays faint, everything else goes as when hidden
                if (!Fading || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                {
                    renderer.enabled = false;
                    undo.Add(() => Enable(renderer));
                    continue;
                }

                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                var faded = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(faded);
                faded.SetFloat(CommonShaderProperties._Fade, Faded);
                renderer.SetPropertyBlock(faded);
                undo.Add(() =>
                {
                    if (renderer)
                    {
                        renderer.SetPropertyBlock(original);
                    }
                });
            }

            DisableColliders(undo);
            StopEffects(undo);
            return () =>
            {
                // Last undone first, so a particle's object is active again before it plays
                for (int i = undo.Count - 1; i >= 0; i--)
                {
                    undo[i]();
                }
            };
        }

        private IEnumerable<T> Own<T>(Component root)
            where T : Component
            => root.GetComponentsInChildren<T>().Where(c => c && !skip.Any(s => s && c.transform.IsChildOf(s.transform)));

        private IEnumerable<Renderer> Renderers()
        {
            ModelLocator locator = GetComponent<ModelLocator>();
            Transform? model = locator ? locator.modelTransform : null;
            return Own<Renderer>(model ? model! : transform);
        }

        private static void Enable(Renderer renderer)
        {
            if (renderer)
            {
                renderer.enabled = true;
            }
        }

        // So you can walk through it and it can't be interacted with by accident
        private void DisableColliders(List<Action> undo)
        {
            foreach (Collider collider in Own<Collider>(transform))
            {
                if (collider.enabled)
                {
                    collider.enabled = false;
                    undo.Add(() =>
                    {
                        if (collider)
                        {
                            collider.enabled = true;
                        }
                    });
                }
            }
        }

        private void StopEffects(List<Action> undo)
        {
            var reactivate = new List<GameObject>();
            var replay = new List<ParticleSystem>();
            foreach (ParticleSystem particles in Own<ParticleSystem>(transform))
            {
                if (particles.isPlaying)
                {
                    replay.Add(particles);
                }

                particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);

                // Never the interactable itself, which would stop it working
                GameObject holder = particles.gameObject;
                if (!Fading && holder != gameObject && holder.activeSelf)
                {
                    holder.SetActive(false);
                    reactivate.Add(holder);
                }
            }

            // Every holder back first, so a child's parent is active when it plays
            undo.Add(() =>
            {
                foreach (GameObject holder in reactivate.Where(h => h))
                {
                    holder.SetActive(true);
                }

                foreach (ParticleSystem particles in replay.Where(p => p))
                {
                    particles.Play(false);
                }
            });

            foreach (TrailRenderer trail in Own<TrailRenderer>(transform))
            {
                if (trail.enabled)
                {
                    trail.enabled = false;
                    undo.Add(() => Enable(trail));
                }
            }

            foreach (Light light in Own<Light>(transform))
            {
                if (light.enabled)
                {
                    light.enabled = false;
                    undo.Add(() =>
                    {
                        if (light)
                        {
                            light.enabled = true;
                        }
                    });
                }
            }
        }
    }
}
