﻿using System;
using OpenUtau.Core;
using OpenUtau.Core.Metronome;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    public class TimeAxisChangedEvent { }
    public class SeekPlayPosChangedEvent {
        public readonly int curTick;
        public SeekPlayPosChangedEvent(int tick) {
            curTick = tick;
        }
    }
    public class PlaybackViewModel : ViewModelBase, ICmdSubscriber {
        UProject Project => DocManager.Inst.Project;
        public int BeatPerBar => Project.timeSignatures[0].beatPerBar;
        public int BeatUnit => Project.timeSignatures[0].beatUnit;
        public double Bpm => Project.tempos[0].bpm;
        public int Key => Project.keys[0].key;
        public string KeyName => MusicMath.KeysInOctave[Key].Item1;
        public int Resolution => Project.resolution;
        public int PlayPosTick => DocManager.Inst.playPosTick;
        public TimeSpan PlayPosTime => TimeSpan.FromMilliseconds((int)Project.timeAxis.TickPosToMsPos(DocManager.Inst.playPosTick));

        public PlaybackViewModel() {
            DocManager.Inst.AddSubscriber(this);
        }

        public void SeekStart() {
            Pause();
            DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(0));
            MessageBus.Current.SendMessage(new SeekPlayPosChangedEvent(0));
        }
        public void SeekEnd() {
            Pause();
            DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(Project.EndTick));
            MessageBus.Current.SendMessage(new SeekPlayPosChangedEvent(Project.EndTick));
        }
        public void PlayOrPause(int tick = -1, int endTick = -1, int trackNo = -1) {
            int curTick = tick == -1 ? DocManager.Inst.playPosTick : tick;
            var timeSignature = Project.GetCurTimeSignatures(curTick);
            MetronomePlayer.Instance.UpdateParmas((int)Project.GetCurTempo(curTick).bpm, timeSignature.beatPerBar, timeSignature.beatUnit);
            PlaybackManager.Inst.PlayOrPause(tick: tick, endTick: endTick, trackNo: trackNo);
            var lockStartTime = Convert.ToBoolean(Preferences.Default.LockStartTime);
            if (!PlaybackManager.Inst.Playing && !PlaybackManager.Inst.StartingToPlay) {
                if (lockStartTime) {
                    DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(PlaybackManager.Inst.StartTick, true));
                } else {
                    MessageBus.Current.SendMessage(new SeekPlayPosChangedEvent(curTick));
                }
            }
        }
        public void Pause() {
            PlaybackManager.Inst.PausePlayback();
        }

        public void MovePlayPos(int tick) {
            if (DocManager.Inst.playPosTick != tick) {
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(Math.Max(0, tick)));
                MessageBus.Current.SendMessage(new SeekPlayPosChangedEvent(Math.Max(0, tick)));
            }
        }

        public void SetTimeSignature(int beatPerBar, int beatUnit) {
            if (beatPerBar > 1 && (beatUnit == 2 || beatUnit == 4 || beatUnit == 8 || beatUnit == 16)) {
                DocManager.Inst.StartUndoGroup();
                DocManager.Inst.ExecuteCmd(new TimeSignatureCommand(Project, beatPerBar, beatUnit));
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void SetBpm(double bpm) {
            if (bpm == DocManager.Inst.Project.tempos[0].bpm) {
                return;
            }
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new BpmCommand(Project, bpm));
            DocManager.Inst.EndUndoGroup();
        }

        public void SetKey(int key) {
            if (key == DocManager.Inst.Project.GetCurKey(PlayPosTick)) {
                return;
            }

            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new KeyCommand(Project, key, PlayPosTick));
            DocManager.Inst.EndUndoGroup();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is BpmCommand ||
                cmd is TimeSignatureCommand ||
                cmd is AddTempoChangeCommand ||
                cmd is DelTempoChangeCommand ||
                cmd is MoveTempoChangeCommand ||
                cmd is AddTimeSigCommand ||
                cmd is DelTimeSigCommand ||
                cmd is MoveTimeSigCommand ||
                cmd is AddKeyChangeCommand ||
                cmd is DelKeyChangeCommand ||
                cmd is MoveKeyChangeCommand ||
                cmd is KeyCommand ||
                cmd is LoadProjectNotification) {
                this.RaisePropertyChanged(nameof(BeatPerBar));
                this.RaisePropertyChanged(nameof(BeatUnit));
                this.RaisePropertyChanged(nameof(Bpm));
                this.RaisePropertyChanged(nameof(KeyName));
                MessageBus.Current.SendMessage(new TimeAxisChangedEvent());
                if (cmd is LoadProjectNotification) {
                    DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(0));
                }
            } else if (cmd is SeekPlayPosTickNotification ||
                cmd is SetPlayPosTickNotification) {
                this.RaisePropertyChanged(nameof(PlayPosTick));
                this.RaisePropertyChanged(nameof(PlayPosTime));
            }
        }
    }
}
