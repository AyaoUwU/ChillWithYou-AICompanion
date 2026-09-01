using AICompanion.Core;
using Bulbul;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AICompanion.Unity
{
    public sealed class UnityCharacterPresentationPipeline : ICharacterPresentationPipeline
    {
        private readonly UnityGameContextProvider _context;
        private HeroineService? _heroine;
        private HeroineService.AnimationType _before;
        private bool _leased;

        public UnityCharacterPresentationPipeline(UnityGameContextProvider context) { _context = context; }

        public async Task PresentAsync(CharacterReplyV1 reply, GameContextSnapshot context, CancellationToken cancellationToken)
        {
            if (!_context.CanTalk(out _)) return;
            _heroine = _context.CurrentHeroine;
            if (_heroine == null) return;
            _before = _heroine.GetCurrentAnimationType();
            _leased = true;

            _heroine.ChangeHeroineFacialAnimation((int)MapEmotion(reply.Emotion));
            var animation = MapAction(reply.Action);
            if (animation != HeroineService.AnimationType.NotChange)
                _heroine.ChangeHeroineAnimationForTrigger(animation);

            await Task.Delay(TimeSpan.FromSeconds(ActionDuration(reply.Action)), cancellationToken);
            RestoreIfOwned();
        }

        public void Release() => RestoreIfOwned();

        private void RestoreIfOwned()
        {
            if (!_leased || _heroine == null) return;
            _leased = false;
            try
            {
                if (_context.CanTalk(out _))
                {
                    _heroine.InitHeroineFacialAnimation();
                    if (_heroine.GetCurrentAnimationType() != _before) _heroine.ChangeAnimation(_before);
                }
            }
            catch { }
        }

        private static FacialType MapEmotion(CharacterEmotion emotion)
        {
            switch (emotion)
            {
                case CharacterEmotion.Joy: return FacialType.Shy_Joy;
                case CharacterEmotion.Fun: return FacialType.Shy_Fun;
                case CharacterEmotion.Shy: return FacialType.Shy_Rest;
                case CharacterEmotion.Sad: return FacialType.Shy_Sad;
                case CharacterEmotion.Anger: return FacialType.Shy_Anger;
                case CharacterEmotion.Surprise: return FacialType.Shy_Surprise;
                default: return FacialType.Open_Eye;
            }
        }

        private static HeroineService.AnimationType MapAction(CharacterAction action)
        {
            switch (action)
            {
                case CharacterAction.Nod: return HeroineService.AnimationType.Base001_Motion12_Start_Nod;
                case CharacterAction.ShakeHead: return HeroineService.AnimationType.Base001_Motion13_Start_ShakeHead;
                case CharacterAction.Think: return HeroineService.AnimationType.Base001_Motion8_Thinking;
                case CharacterAction.Cheer: return HeroineService.AnimationType.Base001_Motion7_Confidence;
                case CharacterAction.Shy: return HeroineService.AnimationType.Base001_Motion5_Shy;
                case CharacterAction.Distress: return HeroineService.AnimationType.Base001_Motion21_Distress;
                default: return HeroineService.AnimationType.NotChange;
            }
        }

        private static double ActionDuration(CharacterAction action) => action == CharacterAction.None ? 1.2 : 3.2;
    }
}
