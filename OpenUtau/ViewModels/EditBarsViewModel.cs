using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using System.Collections.Generic;

namespace OpenUtau.App.ViewModels {
    class EditBarsViewModel : ViewModelBase {
        public int startBar { get; set; } = 0;
        public int barsCount { get; set; } = 2;
        public int handleRange { get; set; } = 0;
        public int handleType { get; set; } = 0;
        public bool handleRangeEnable { get; set; } = true;

        private UProject project;
        private int trackNo;
        private int postion;
        private int barLength = 0;
        public string Title { get; set; } = "";

        public EditBarsViewModel(UProject project, int trackNo, int postion) {
            this.project = project;
            this.trackNo = trackNo;
            this.postion = postion;
            this.project.timeAxis.TickPosToBarBeat(this.postion, out int timebar, out int _, out int _);

            startBar = timebar + 1;
            barLength = this.project.timeAxis.GetBarLengthAtTick(this.postion);
        }
        public void DeleteBars() {
            if (this.project == null) { return; }
            int startPos = this.project.timeAxis.BarBeatToTickPos((startBar - 1), 0);
            int handleLen = barsCount * barLength;

            List<UVoicePart> handleParts = new List<UVoicePart>();
            foreach (var cur_part in this.project.parts) {
                if (cur_part is not UVoicePart) continue;
                if (handleRange == 0 && cur_part.trackNo != this.trackNo) {
                    continue;
                }
                if (cur_part.End < startPos) continue;
                handleParts.Add((UVoicePart)cur_part);
            }

            foreach (var cur_part in handleParts) {

                if (cur_part.position <= startPos) {
                    // 如果要插入空白的位置在part中间，就需要细节处理，移动音符、标签、曲线等
                    if(cur_part.duration <= handleLen) continue;
                    DocManager.Inst.ExecuteCmd(new ResizePartCommand(this.project, cur_part, cur_part.duration - handleLen));

                    // 如果note在删除的小节范围内，那就把note也删除
                    List<UNote> deleteNotes = new List<UNote>();
                    foreach (var note in cur_part.notes) {
                        if (note.position + cur_part.position >= startPos && note.End + cur_part.position <= startPos + handleLen) {
                            deleteNotes.Add(note);
                        }
                    }
                    if (deleteNotes.Count > 0) {
                        DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(cur_part, deleteNotes));
                    }

                    List<UNote> moveNotes = new List<UNote>();
                    foreach (var note in cur_part.notes) {
                        if (note.position + cur_part.position >= startPos) {
                            moveNotes.Add(note);
                        }
                    }
                    if (moveNotes.Count > 0) {
                        DocManager.Inst.ExecuteCmd(new MoveNoteCommand(cur_part, moveNotes, -handleLen, 0));
                    }
                    for (int i = cur_part.remarks.Count - 1; i >= 0; i--) {
                        var remark = cur_part.remarks[i];
                        if(remark.position + cur_part.position >= startPos && remark.position + cur_part.position <= startPos + handleLen) {
                            DocManager.Inst.ExecuteCmd(new DeleteRemarkCommand(cur_part, remark, i));
                        }
                        else if (remark.position + cur_part.position > startPos + handleLen) {
                            int newPos = remark.position - handleLen;
                            if(newPos >= 0) {
                                DocManager.Inst.ExecuteCmd(new MoveRemarkCommand(cur_part, remark, i, newPos));
                            }
                        }
                    }
                    // 删除范围内的曲线点
                    foreach (var curve in cur_part.curves) {
                        var xs = curve.xs.ToArray();
                        var ys = curve.ys.ToArray();
                        bool hasChange = false;
                        List<int> deletePoints = new List<int>();
                        for (int i = xs.Length - 1; i >= 0; i--) {
                            if (xs[i] + cur_part.position >= startPos && xs[i] + cur_part.position <= startPos + handleLen) {
                                deletePoints.Add(i);
                                hasChange = true;
                            }
                        }
                        if (hasChange) {
                            DocManager.Inst.ExecuteCmd(new DeleteCurvePointsCommand(this.project, cur_part, curve.abbr, deletePoints.ToArray()));
                        }
                    }
                    foreach (var curve in cur_part.curves) {
                        var xs = curve.xs.ToArray();
                        var ys = curve.ys.ToArray();
                        bool hasChange = false;
                        for (int i = xs.Length - 1; i >= 0; i--) {
                            if (xs[i] + cur_part.position > startPos + handleLen) {
                                xs[i] = xs[i] - handleLen;
                                hasChange = true;
                            }
                        }
                        if (hasChange) {
                            DocManager.Inst.ExecuteCmd(new MoveCurvePointsCommand(this.project, cur_part, curve.abbr, xs, ys));
                        }
                    }
                }
            }
            foreach (var cur_part in handleParts) {
                if (cur_part.position > startPos) {
                    // 如果part在要删除空白的后面，直接把part往前移就可以了
                    if(cur_part.position <= handleLen) continue;
                    DocManager.Inst.ExecuteCmd(new MovePartCommand(this.project, cur_part, cur_part.position - handleLen, cur_part.trackNo));
                }
            }

            // 移动timeSignatures、tempos、keys
            if (handleRange != 0) {
                for (int i = 0; i < this.project.timeSignatures.Count; i++) {
                    var timeSignature = this.project.timeSignatures[i];
                    if (timeSignature.barPosition > (startBar - 1) + barsCount) {
                        DocManager.Inst.ExecuteCmd(new MoveTimeSigCommand(this.project, i, -barsCount));
                    }
                }
                for (int i = 0; i < this.project.tempos.Count; i++) {
                    var tempo = this.project.tempos[i];
                    if (tempo.position > startPos + handleLen) {
                        DocManager.Inst.ExecuteCmd(new MoveTempoChangeCommand(this.project, i, -handleLen));
                    }
                }
                for (int i = 0; i < this.project.keys.Count; i++) {
                    var key = this.project.keys[i];
                    if (key.position > startPos + handleLen) {
                        DocManager.Inst.ExecuteCmd(new MoveKeyChangeCommand(this.project, i, -handleLen));
                    }
                }
            } 
        }
        public void InsertBars() {
            
            if (this.project == null) { return; }
            int startPos = this.project.timeAxis.BarBeatToTickPos((startBar - 1), 0);
            int handleLen = barsCount * barLength;
            
            // 获取要处理的part
            List<UVoicePart> handleParts = new List<UVoicePart>();
            foreach (var cur_part in this.project.parts) {
                if (cur_part is not UVoicePart) continue;
                if (handleRange == 0 && cur_part.trackNo != this.trackNo) {
                    continue;
                }
                if (cur_part.End < startPos) continue;
                handleParts.Add((UVoicePart)cur_part);
            }
            // 移动part
            foreach (var cur_part in handleParts) {
                if (cur_part.position > startPos) {
                    // 如果part在要插入空白的后面，直接把part往后移就可以了
                    DocManager.Inst.ExecuteCmd(new MovePartCommand(this.project, cur_part, cur_part.position + handleLen, cur_part.trackNo));
                }
            }
            // 移动part内的东西
            foreach (var cur_part in handleParts) {
                
                if (cur_part.position <= startPos) {
                    // 如果要插入空白的位置在part中间，就需要细节处理，移动音符、标签、曲线等
                    DocManager.Inst.ExecuteCmd(new ResizePartCommand(this.project, cur_part, cur_part.duration + handleLen));
                    // 移动音符
                    List<UNote> moveNotes = new List<UNote>();
                    foreach (var note in cur_part.notes) {
                        if (note.position + cur_part.position >= startPos) {
                            moveNotes.Add(note);
                        }
                    }
                    if (moveNotes.Count > 0) {
                        DocManager.Inst.ExecuteCmd(new MoveNoteCommand(cur_part, moveNotes, handleLen, 0));
                    }

                    // 移动remarts
                    for (int i = 0; i < cur_part.remarks.Count; i++) {
                        var remark = cur_part.remarks[i];
                        if (remark.position + cur_part.position >= startPos) {
                            int newPos = remark.position + handleLen;
                            DocManager.Inst.ExecuteCmd(new MoveRemarkCommand(cur_part, remark, i, newPos));
                        }
                    }

                    // 移动曲线
                    foreach (var curve in cur_part.curves) {
                        var xs = curve.xs.ToArray();
                        var ys = curve.ys.ToArray();
                        bool hasChange = false;
                        for (int i = 0; i < xs.Length; i++) {
                            if (xs[i] + cur_part.position >= startPos) {
                                xs[i] = xs[i] + handleLen;
                                hasChange = true;
                            }
                        }
                        if (hasChange) {
                            DocManager.Inst.ExecuteCmd(new MoveCurvePointsCommand(this.project, cur_part, curve.abbr, xs, ys));
                        }
                    }
                }
            }

            // 移动timeSignatures、tempos、keys
            if (handleRange != 0) {
                for (int i = 0; i < this.project.timeSignatures.Count; i++) {
                    var timeSignature = this.project.timeSignatures[i];
                    if (timeSignature.barPosition > (startBar - 1)) {
                        DocManager.Inst.ExecuteCmd(new MoveTimeSigCommand(this.project, i, barsCount));
                    }
                }
                for (int i = 0; i < this.project.tempos.Count; i++) {
                    var tempo = this.project.tempos[i];
                    if (tempo.position > startPos) {
                        DocManager.Inst.ExecuteCmd(new MoveTempoChangeCommand(this.project, i, handleLen));
                    }
                }
                for (int i = 0; i < this.project.keys.Count; i++) {
                    var key = this.project.keys[i];
                    if (key.position > startPos) {
                        DocManager.Inst.ExecuteCmd(new MoveKeyChangeCommand(this.project, i, handleLen));
                    }
                }
            }
        }
        public void Cancel() {
        }
        public void Finish() {
            DocManager.Inst.StartUndoGroup();
            if (handleType == 0) {
                InsertBars();
            } else {
                DeleteBars();
            }
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new CurvesRefreshEvent());
        }
    }
}
