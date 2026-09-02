#nullable enable
using System.Runtime.CompilerServices;
using StardewModdingAPI;
using StardewValley.Menus;

namespace PlayerPortraitsFramework
{
    /// <summary>
    /// TEMPORARY instrumentation for the question-box pass. Answers two questions that could not be
    /// settled by reading the decompiled game:
    ///   1. Does every portrait-bearing question path REUSE the same <see cref="DialogueBox"/>
    ///      instance? A fresh instance fires MenuChanged, which resets the animation cursor.
    ///   2. What is the real <c>heightForQuestions</c> vs <c>height</c> for questions of different
    ///      sizes? Those numbers decide the top-pin vs bottom-pin tradeoff later.
    ///
    /// <para>STRIP THIS FILE (and its three call sites) before merging — it logs at Info so the
    /// numbers are readable without turning on verbose SMAPI logging.</para>
    /// </summary>
    internal static class QuestionBoxDiagnostics
    {
        /// <summary>Reference identity of the last box seen drawing NORMAL (non-question) dialogue.</summary>
        private static int _lastNormalBoxId;

        /// <summary>Signature of the last question episode logged, so each distinct question logs once.</summary>
        private static string _lastQuestionSignature = "";

        /// <summary>Reference identity — NOT Equals-based — so two distinct boxes never collide.</summary>
        private static int IdOf(object? o) => o is null ? 0 : RuntimeHelpers.GetHashCode(o);

        /// <summary>Records the box currently drawing normal dialogue, for later instance comparison.</summary>
        internal static void NoteNormalBox(DialogueBox box) => _lastNormalBoxId = IdOf(box);

        /// <summary>
        /// Logs one line per distinct question episode: instance identity (vs the last normal box),
        /// the option count, and the real box measurements.
        /// </summary>
        internal static void NoteQuestion(DialogueBox box)
        {
            int id           = IdOf(box);
            int responses    = box.responses?.Length ?? 0;
            string signature = $"{id}:{responses}:{box.heightForQuestions}";
            if (signature == _lastQuestionSignature)
                return;
            _lastQuestionSignature = signature;

            bool sameInstance = id == _lastNormalBoxId && _lastNormalBoxId != 0;
            string verdict = sameInstance
                ? "SAME instance as the preceding dialogue → no MenuChanged → animation continues"
                : "DIFFERENT instance (or no preceding dialogue) → MenuChanged fired → animation RESET";

            // heightForQuestions is what the question box actually draws with; height is what a normal
            // box uses. The box grows UPWARD by the delta, so delta is exactly how far the top edge
            // travels into the top-pinned portraits.
            ModEntry.SMonitor.Log(
                $"[Q-DIAG] box#{id} — {verdict}"
                + $" | options={responses}"
                + $" | height={box.height} heightForQuestions={box.heightForQuestions} delta={box.heightForQuestions - box.height}"
                + $" | width={box.width} x={box.x} y={box.y}"
                + $" | speaker={box.characterDialogue?.speaker?.Name ?? "(none)"}"
                + $" portraitIndex={box.characterDialogue?.getPortraitIndex().ToString() ?? "(n/a)"}",
                LogLevel.Info);
        }

        /// <summary>Logs dialogue box open/close with instance identity, to catch fresh-box construction.</summary>
        internal static void NoteMenuChanged(object? oldMenu, object? newMenu)
        {
            if (oldMenu is not DialogueBox && newMenu is not DialogueBox)
                return;

            ModEntry.SMonitor.Log(
                $"[Q-DIAG] MenuChanged — old={(oldMenu is DialogueBox ? $"DialogueBox#{IdOf(oldMenu)}" : oldMenu?.GetType().Name ?? "null")}"
                + $" new={(newMenu is DialogueBox ? $"DialogueBox#{IdOf(newMenu)}" : newMenu?.GetType().Name ?? "null")}"
                + $" | isQuestion={(newMenu as DialogueBox)?.isQuestion.ToString() ?? "(n/a)"}",
                LogLevel.Info);
        }
    }
}
