using System.Collections.Generic;
using OuterCraft.Assets;
using UnityEngine;

namespace OuterCraft.Player
{
    /// Hearthians (and the other travellers) talk like Minecraft villagers: a "hmm" when a
    /// conversation starts and on every new page of dialogue, from where they stand.
    public sealed class VillagerVoices
    {
        public bool Enabled = true;
        private readonly List<(CharacterDialogueTree tree, CharacterDialogueTree.ConversationEvent start, CharacterDialogueTree.ConversationPageEvent page)> _hooks
            = new List<(CharacterDialogueTree, CharacterDialogueTree.ConversationEvent, CharacterDialogueTree.ConversationPageEvent)>();
        private static readonly System.Random Rng = new System.Random();
        private float _last;

        public void Hook()
        {
            Unhook();
            foreach (var anim in Object.FindObjectsOfType<CharacterAnimController>())
            {
                var tree = anim._dialogueTree;
                if (tree == null) continue;
                var who = anim.transform;
                CharacterDialogueTree.ConversationEvent start = () => Hmm(who);
                CharacterDialogueTree.ConversationPageEvent page = (node, num) => Hmm(who);
                tree.OnStartConversation += start;
                tree.OnAdvancePage += page;
                _hooks.Add((tree, start, page));
            }
        }

        public void Unhook()
        {
            foreach (var (tree, start, page) in _hooks)
            {
                if (tree == null) continue;
                tree.OnStartConversation -= start;
                tree.OnAdvancePage -= page;
            }
            _hooks.Clear();
        }

        private void Hmm(Transform who)
        {
            if (!Enabled || who == null || Time.unscaledTime - _last < 0.15f) return;
            _last = Time.unscaledTime;
            // Villager ambient sound: volume 1, pitch (rand - rand) * 0.2 + 1
            float pitch = (float)(Rng.NextDouble() - Rng.NextDouble()) * 0.2f + 1f;
            McSounds.Play("villager.idle", who.position + who.up * 1.5f, who, 1f, pitch);
        }
    }
}
