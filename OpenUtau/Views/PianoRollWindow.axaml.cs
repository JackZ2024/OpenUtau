﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Newtonsoft.Json;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using Serilog;
using OpenUtau.Core.Metronome;
using Avalonia.Threading;

namespace OpenUtau.App.Views {
    interface IValueTip {
        void ShowValueTip();
        void HideValueTip();
        void UpdateValueTip(string text);
    }

    public partial class PianoRollWindow : Window, IValueTip, ICmdSubscriber {
        public MainWindow? MainWindow { get; set; }
        public readonly PianoRollViewModel ViewModel;

        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        private KeyboardPlayState? keyboardPlayState;
        private NoteEditState? editState;
        private Point valueTipPointerPosition;
        private bool shouldOpenNotesContextMenu;
        private DispatcherTimer _remarkTimer;
        private Point lastPoint;
        private URemark? CopyRemark;

        private ReactiveCommand<Unit, Unit> lyricsDialogCommand;
        private ReactiveCommand<Unit, Unit> noteDefaultsCommand;
        private ReactiveCommand<BatchEdit, Unit> noteBatchEditCommand;

        public ReactiveCommand<int, Unit> RemarkAddCommand;
        public ReactiveCommand<RemarkHitInfo, Unit> RemarkEditCommand;
        public ReactiveCommand<RemarkHitInfo, Unit> RemarkDelCommand;
        public ReactiveCommand<RemarkHitInfo, Unit> RemarkCopyCommand;
        public ReactiveCommand<int, Unit> RemarkPasteCommand;

        public ReactiveCommand<int, Unit> BarsEditCommand;

        public PianoRollWindow() {
            InitializeComponent();
            DataContext = ViewModel = new PianoRollViewModel();
            ValueTip.IsVisible = false;
            _remarkTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _remarkTimer.Tick += HandleShowRemark;

            Dictionary<string, string> gestureDict = new Dictionary<string, string> {
                { "pianoroll.menu.notes.loadrenderedpitch", "Ctrl+R" },
                { "pianoroll.menu.notes.loadrenderedpitchforSelectedNote", "Shift+R" },
                { "pianoroll.menu.notes.autolegato", "Ctrl+L" },

                { "pianoroll.menu.notes.reset.pitchbends", "Alt+B" },
                { "pianoroll.menu.notes.reset.exps", "Alt+E" },
                { "pianoroll.menu.notes.clear.vibratos", "Alt+C" },
                { "pianoroll.menu.notes.reset.vibratos", "Alt+R" }
             };

            try {
                string keyGesturePath = Path.Combine(PathManager.Inst.DataPath, "keyGesture.json");
                string json = File.ReadAllText(keyGesturePath);
                
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (dictionary != null) {
                    gestureDict = dictionary;
                }
            }
            catch { }

            noteBatchEditCommand = ReactiveCommand.Create<BatchEdit>(async edit => {
                var NotesVm = ViewModel?.NotesViewModel;
                if (NotesVm == null || NotesVm.Part == null) {
                    return;
                }
                try {
                    if (edit.IsAsync) {
                        var mainWindow =
                            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                            ?.MainWindow! as MainWindow;
                        var name = ThemeManager.GetString(edit.Name);
                        await MessageBox.ShowProcessing(this, $"{name} - ? / ?",
                            ThemeManager.GetString("pianoroll.menu.batch.running"),
                            (messageBox, cancellationToken) => {
                                edit.RunAsync(NotesVm.Project, NotesVm.Part,
                                    NotesVm.Selection.ToList(), DocManager.Inst,
                                    (current, total) => {
                                        messageBox.SetText($"{name}: {current} / {total}");
                                    }, cancellationToken);
                            },
                            (Task t) => {
                                var e = t.Exception;
                                if (t.IsFaulted && e != null) {
                                    if (e != null) {
                                        Log.Error(e, $"Failed to run Editing Macro");
                                        var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                                    }
                                    return;
                                }
                            }
                        );
                    } else {
                        edit.Run(NotesVm.Project, NotesVm.Part, NotesVm.Selection.ToList(),
                            DocManager.Inst);
                    }
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }

            });
            ViewModel.NoteBatchEdits.AddRange(new List<BatchEdit>() {
                new LoadRenderedPitch(),
                new LoadRenderedPitchForSelectedNote(),
                new AddTailNote("-", "pianoroll.menu.notes.addtaildash"),
                new AddTailNote("R", "pianoroll.menu.notes.addtailrest"),
                new RemoveTailNote("-", "pianoroll.menu.notes.removetaildash"),
                new RemoveTailNote("R", "pianoroll.menu.notes.removetailrest"),
                new Transpose(12, "pianoroll.menu.notes.octaveup"),
                new Transpose(-12, "pianoroll.menu.notes.octavedown"),
                new AutoLegato(),
                new FixOverlap(),
                new BakePitch(),
            }.Select(edit => new MenuItemViewModel() {
                Header = ThemeManager.GetString(edit.Name),
                Command = noteBatchEditCommand,
                CommandParameter = edit,
                InputGesture = gestureDict.ContainsKey(edit.Name) ? KeyGesture.Parse(gestureDict[edit.Name]) : null,
            }));
            ViewModel.LyricBatchEdits.AddRange(new List<BatchEdit>() {
                new RomajiToHiragana(),
                new HiraganaToRomaji(),
                new JapaneseVCVtoCV(),
                new HanziToPinyin(),
                new RemoveToneSuffix(),
                new RemoveLetterSuffix(),
                new MoveSuffixToVoiceColor(),
                new RemovePhoneticHint(),
                new DashToPlus(),
                new InsertSlur(),
            }.Select(edit => new MenuItemViewModel() {
                Header = ThemeManager.GetString(edit.Name),
                Command = noteBatchEditCommand,
                CommandParameter = edit,
                InputGesture = gestureDict.ContainsKey(edit.Name) ? KeyGesture.Parse(gestureDict[edit.Name]) : null,
            }));
            ViewModel.ResetBatchEdits.AddRange(new List<BatchEdit>() {
                new ResetAll(),
                new ResetPitchBends(),
                new ResetAllExpressions(),
                new ClearVibratos(),
                new ResetVibratos(),
                new ClearTimings(),
                new ResetAliases(),
                new ResetPhonemes(),
            }.Select(edit => new MenuItemViewModel() {
                Header = ThemeManager.GetString(edit.Name),
                Command = noteBatchEditCommand,
                CommandParameter = edit,
                InputGesture = gestureDict.ContainsKey(edit.Name) ? KeyGesture.Parse(gestureDict[edit.Name]) : null,
            }));
            DocManager.Inst.AddSubscriber(this);

            ViewModel.NoteBatchEdits.Insert(6, new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.addbreath"),
                InputGesture = gestureDict.ContainsKey("pianoroll.menu.notes.addbreath") ? KeyGesture.Parse(gestureDict["pianoroll.menu.notes.addbreath"]) : null,
                Command = ReactiveCommand.Create(() => {
                    AddBreathNote();
                })
            });
            ViewModel.NoteBatchEdits.Insert(7, new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.deleteallbreath"),
                InputGesture = gestureDict.ContainsKey("pianoroll.menu.notes.deleteallbreath") ? KeyGesture.Parse(gestureDict["pianoroll.menu.notes.deleteallbreath"]) : null,
                Command = ReactiveCommand.Create(() => {
                    DelBreathNote(true);
                })
            });
            ViewModel.NoteBatchEdits.Insert(8, new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.deletebreathonlyselected"),
                InputGesture = gestureDict.ContainsKey("pianoroll.menu.notes.deletebreathonlyselected") ? KeyGesture.Parse(gestureDict["pianoroll.menu.notes.deletebreathonlyselected"]) : null,
                Command = ReactiveCommand.Create(() => {
                    DelBreathNote(false);
                })
            });
            ViewModel.NoteBatchEdits.Insert(9, new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.quantize"),
                Command = ReactiveCommand.Create(() => {
                    QuantizeNotes();
                })
            });
            ViewModel.NoteBatchEdits.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.lengthencrossfade"),
                InputGesture = gestureDict.ContainsKey("pianoroll.menu.notes.lengthencrossfade") ? KeyGesture.Parse(gestureDict["pianoroll.menu.notes.lengthencrossfade"]) : null,
                Command = ReactiveCommand.Create(() => {
                    LengthenCrossfade();
                })
            });
            ViewModel.LyricBatchEdits.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("lyricsreplace.replace"),
                InputGesture = gestureDict.ContainsKey("lyricsreplace.replace") ? KeyGesture.Parse(gestureDict["lyricsreplace.replace"]) : null,
                Command = ReactiveCommand.Create(() => {
                    ReplaceLyrics();
                })
            });
            lyricsDialogCommand = ReactiveCommand.Create(() => {
                EditLyrics();
            });
            noteDefaultsCommand = ReactiveCommand.Create(() => {
                EditNoteDefaults();
            });
            RemarkAddCommand = ReactiveCommand.Create<int>(pos => {
                if (ViewModel?.NotesViewModel.Part == null) { return; }
                var vm = new RemarkViewModel(ViewModel.NotesViewModel.Part, new URemark(), pos, -1);
                vm.Title = ThemeManager.GetString("remark.add");
                var dialog = new RemarkDialog() {
                    DataContext = vm,
                };
                dialog.ShowDialog(this);
            });
            RemarkEditCommand = ReactiveCommand.Create<RemarkHitInfo>(info => {
                if (ViewModel?.NotesViewModel.Part == null) { return; }
                var remark = info.Remark;
                var vm = new RemarkViewModel(ViewModel.NotesViewModel.Part, remark, remark.position, info.Index);
                vm.Title = ThemeManager.GetString("remark.edit");
                var dialog = new RemarkDialog() {
                    DataContext = vm,
                };
                dialog.ShowDialog(this);
            });
            RemarkDelCommand = ReactiveCommand.Create<RemarkHitInfo>(info => {
                if (ViewModel?.NotesViewModel.Part == null) { return; }
                var remark = info.Remark;
                var vm = new RemarkViewModel(ViewModel.NotesViewModel.Part, remark, remark.position, info.Index);
                vm.DeleteRemark();
            });
            RemarkCopyCommand = ReactiveCommand.Create<RemarkHitInfo>(info => {
                if (ViewModel?.NotesViewModel.Part == null) { return; }
                CopyRemark = info.Remark.Clone();
            });
            RemarkPasteCommand = ReactiveCommand.Create<int>(pos => {
                if (ViewModel?.NotesViewModel.Part == null || CopyRemark == null) { return; }
                DocManager.Inst.StartUndoGroup();
                CopyRemark.position = pos;
                DocManager.Inst.ExecuteCmd(new AddRemarkCommand(ViewModel.NotesViewModel.Part, CopyRemark));
                DocManager.Inst.EndUndoGroup();
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
            });

            BarsEditCommand = ReactiveCommand.Create<int>(pos => {
                if (ViewModel?.NotesViewModel.Part == null) { return; }
                var vm = new EditBarsViewModel(ViewModel.NotesViewModel.Project, 
                                                ViewModel.NotesViewModel.Part.trackNo, 
                                                ViewModel.NotesViewModel.Part.position + pos);
                vm.Title = ThemeManager.GetString("editbar.editbar");
                vm.handleRange = 0;
                vm.handleRangeEnable = true;
                var dialog = new EditBarsDialog() {
                    DataContext = vm,
                };
                dialog.ShowDialog(this);
            });

            DocManager.Inst.AddSubscriber(this);
        }

        public void WindowDeactivated(object sender, EventArgs args) {
            LyricBox?.EndEdit();
        }

        void WindowClosing(object? sender, WindowClosingEventArgs e) {
            Hide();
            e.Cancel = true;
        }

        void OnMenuClosed(object sender, RoutedEventArgs args) {
            Focus(); // Force unfocus menu for key down events.
        }

        void OnMenuPointerLeave(object sender, PointerEventArgs args) {
            Focus(); // Force unfocus menu for key down events.
        }

        // Edit menu
        void OnMenuLockPitchPoints(object sender, RoutedEventArgs args) {
            Preferences.Default.LockUnselectedNotesPitch = !Preferences.Default.LockUnselectedNotesPitch;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.LockPitchPoints));
        }
        void OnMenuLockVibrato(object sender, RoutedEventArgs args) {
            Preferences.Default.LockUnselectedNotesVibrato = !Preferences.Default.LockUnselectedNotesVibrato;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.LockVibrato));
        }
        void OnMenuLockExpressions(object sender, RoutedEventArgs args) {
            Preferences.Default.LockUnselectedNotesExpressions = !Preferences.Default.LockUnselectedNotesExpressions;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.LockExpressions));
        }

        // View menu
        void OnMenuShowPortrait(object sender, RoutedEventArgs args) {
            Preferences.Default.ShowPortrait = !Preferences.Default.ShowPortrait;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.ShowPortrait));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
        }
        void OnMenuShowIcon(object sender, RoutedEventArgs args) {
            Preferences.Default.ShowIcon = !Preferences.Default.ShowIcon;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.ShowIcon));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
        }
        void OnMenuShowGhostNotes(object sender, RoutedEventArgs args) {
            Preferences.Default.ShowGhostNotes = !Preferences.Default.ShowGhostNotes;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.ShowGhostNotes));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));

        }
        void OnMenuUseTrackColor(object sender, RoutedEventArgs args) {
            Preferences.Default.UseTrackColor = !Preferences.Default.UseTrackColor;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.UseTrackColor));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
        }
        void OnMenuFullScreen(object sender, RoutedEventArgs args) {
            this.WindowState = this.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        }
        void OnMenuDegreeStyle(object sender, RoutedEventArgs args) {
            if (sender is MenuItem menu && int.TryParse(menu.Tag?.ToString(), out int tag)) {
                Preferences.Default.DegreeStyle = tag;
                Preferences.Save();
                ViewModel.RaisePropertyChanged(nameof(ViewModel.DegreeStyle0));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.DegreeStyle1));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.DegreeStyle2));
                MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
            }
        }
        void OnMenuLockStartTime(object sender, RoutedEventArgs args) {
            if (sender is MenuItem menu && int.TryParse(menu.Tag?.ToString(), out int tag)) {
                Preferences.Default.LockStartTime = tag;
                Preferences.Save();
                ViewModel.RaisePropertyChanged(nameof(ViewModel.LockStartTime0));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.LockStartTime1));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.LockStartTime2));
            }
        }
        void OnMenuPlaybackAutoScroll(object sender, RoutedEventArgs args) {
            if (sender is MenuItem menu && int.TryParse(menu.Tag?.ToString(), out int tag)) {
                Preferences.Default.PlaybackAutoScroll = tag;
                Preferences.Save();
                ViewModel.RaisePropertyChanged(nameof(ViewModel.PlaybackAutoScroll0));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.PlaybackAutoScroll1));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.PlaybackAutoScroll2));
            }
        }

        void OnMenuSingers(object sender, RoutedEventArgs args) {
            MainWindow?.OpenSingersWindow();
            this.Activate();
            try {
                USinger? singer = null;
                UOto? oto = null;
                if (ViewModel.NotesViewModel.Part != null) {
                    singer = ViewModel.NotesViewModel.Project.tracks[ViewModel.NotesViewModel.Part.trackNo].Singer;
                    if (!ViewModel.NotesViewModel.Selection.IsEmpty && ViewModel.NotesViewModel.Part.phonemes.Count() > 0) {
                        oto = ViewModel.NotesViewModel.Part.phonemes.First(p => p.Parent == ViewModel.NotesViewModel.Selection.First()).oto;
                    }
                }
                DocManager.Inst.ExecuteCmd(new GotoOtoNotification(singer, oto));
            } catch { }
        }

        void OnMenuSearchNote(object sender, RoutedEventArgs args) {
            SearchNote();
        }

        void SearchNote() {
            if (ViewModel.NotesViewModel.Part == null || ViewModel.NotesViewModel.Part.notes.Count == 0) {
                return;
            }
            SearchBar.Show(ViewModel.NotesViewModel);
        }

        void ReplaceLyrics() {
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var (notes, lyrics) = ViewModel.NotesViewModel.PrepareInsertLyrics();
            var vm = new LyricsReplaceViewModel(ViewModel.NotesViewModel.Part, notes, lyrics);
            var dialog = new LyricsReplaceDialog() {
                DataContext = vm,
            };
            dialog.ShowDialog(this);
        }

        void OnMenuEditLyrics(object? sender, RoutedEventArgs e) {
            EditLyrics();
        }

        void EditLyrics() {
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var vm = new LyricsViewModel();
            var (notes, lyrics) = ViewModel.NotesViewModel.PrepareInsertLyrics();
            vm.Start(ViewModel.NotesViewModel.Part, notes, lyrics);
            var dialog = new LyricsDialog() {
                DataContext = vm,
            };
            dialog.ShowDialog(this);
        }

        void OnMenuNoteDefaults(object sender, RoutedEventArgs args) {
            EditNoteDefaults();
        }

        void EditNoteDefaults() {
            var dialog = new NoteDefaultsDialog();
            dialog.ShowDialog(this);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        void OnMenuCurveEdit(object sender, RoutedEventArgs args) {
            var dialog = new BatchAdjustCurve() {
                DataContext = ViewModel.NotesViewModel
            };
            dialog.ShowDialog(this);
        }

        void AddBreathNote() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            if (notesVM.Selection.IsEmpty) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var dialog = new TypeInDialog() {
                Title = ThemeManager.GetString("pianoroll.menu.notes.addbreath"),
                onFinish = value => {
                    if (!string.IsNullOrWhiteSpace(value)) {
                        var edit = new Core.Editing.AddBreathNote(value);
                        try {
                            edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
                        } catch (Exception e) {
                            var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                        }
                    }
                }
            };
            dialog.SetText("br");
            dialog.ShowDialog(this);
        }
        void DelBreathNote(bool all=true) {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            if (!all && notesVM.Selection.IsEmpty) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("pianoroll.menu.notes.deletebreathonlyselected"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }

            var dialog = new TypeInDialog() {
                Title = all ? ThemeManager.GetString("pianoroll.menu.notes.deleteallbreath") : 
                                ThemeManager.GetString("pianoroll.menu.notes.deletebreathonlyselected"),
                onFinish = value => {
                    Console.WriteLine(value);
                    var notes = notesVM.Selection.ToList();
                    if (all) {
                        notes = notesVM.Part.notes.ToList();
                    }

                    List<UNote> breaths = new List<UNote>();
                    if (notes == null) { return; }
                    var breathNotes = value.Replace("，", ",").Split(",");
                    foreach (UNote note in notes) {
                        foreach (var breathNote in breathNotes) {
                            if (breathNote != "" && note.lyric == breathNote) {
                                breaths.Add(note);
                                break;
                            }
                        }
                    }

                    DocManager.Inst.StartUndoGroup();
                    DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(notesVM.Part, breaths));
                    DocManager.Inst.EndUndoGroup();
                }
            };
            dialog.SetText(Preferences.Default.BreathNoteString);
            dialog.ShowDialog(this);
        }

        void QuantizeNotes() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            var edit = new QuantizeNotes(notesVM.Project.resolution * 4 / notesVM.SnapDiv);
            edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
        }

        void LengthenCrossfade() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            if (notesVM.Selection.IsEmpty) {
                _ = MessageBox.Show(
                    this,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var dialog = new SliderDialog(ThemeManager.GetString("pianoroll.menu.notes.lengthencrossfade"), 0.5, 0, 1, 0.1);
            dialog.onFinish = value => {
                var edit = new Core.Editing.LengthenCrossfade(value);
                try {
                    edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            };
            dialog.ShowDialog(this);
        }

        public void OnExpButtonClick(object sender, RoutedEventArgs args) {
            var dialog = new ExpressionsDialog() {
                DataContext = new ExpressionsViewModel(),
            };
            dialog.ShowDialog(this);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        public void KeyboardPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            VScrollPointerWheelChanged(VScrollBar, args);
        }

        public void KeyboardPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (keyboardPlayState != null) {
                return;
            }
            var element = (TrackBackground)sender;
            keyboardPlayState = new KeyboardPlayState(element, ViewModel);
            keyboardPlayState.Begin(args.Pointer, args.GetPosition(element));
        }

        public void KeyboardPointerMoved(object sender, PointerEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.Update(args.Pointer, args.GetPosition(element));
            }
        }

        public void KeyboardPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.End(args.Pointer, args.GetPosition(element));
                keyboardPlayState = null;
            }
        }

        public void HScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            LyricBox?.EndEdit();
        }

        public void VScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            LyricBox?.EndEdit();
        }

        public void TimelinePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var control = (Control)sender;
            var position = args.GetCurrentPoint((Visual)sender).Position;
            var size = control.Bounds.Size;
            position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
            ViewModel.NotesViewModel.OnXZoomed(position, 0.1 * args.Delta.Y);
            LyricBox?.EndEdit();
        }

        public void ViewScalerPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            ViewModel.NotesViewModel.OnYZoomed(new Point(0, 0.5), 0.1 * args.Delta.Y);
            LyricBox?.EndEdit();
        }

        public void TimelinePointerPressed(object sender, PointerPressedEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                args.Pointer.Capture(control);
                ViewModel.NotesViewModel.PointToLineTick(point.Position, out int left, out int right);
                int tick = left + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            } else if (point.Properties.IsRightButtonPressed) {
                lastPoint = new Point();
                var remarkHitInfo = ViewModel.NotesViewModel.HitTest.HitTestRemark(point.Position);
                if (remarkHitInfo.OnPoint && ViewModel?.NotesViewModel?.Part != null) {
                    ViewModel.RemarkContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("remark.edit"),
                        Command = RemarkEditCommand,
                        CommandParameter = remarkHitInfo,
                    });
                    ViewModel.RemarkContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("remark.delete"),
                        Command = RemarkDelCommand,
                        CommandParameter = remarkHitInfo,
                    });
                    ViewModel.RemarkContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("menu.edit.copy"),
                        Command = RemarkCopyCommand,
                        CommandParameter = remarkHitInfo,
                    });
                } else {
                    if (ViewModel?.NotesViewModel.Part == null) {
                        return;
                    }
                    int tick = ViewModel.NotesViewModel.PointToTick(point.Position);
                    ViewModel.RemarkContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("remark.add"),
                        Command = RemarkAddCommand,
                        CommandParameter = tick,
                    });
                    ViewModel.RemarkContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("menu.edit.paste"),
                        Command = RemarkPasteCommand,
                        CommandParameter = tick,
                    });
                }

                int cur_tick = ViewModel.NotesViewModel.PointToTick(point.Position);
                ViewModel.RemarkContextMenuItems.Add(new MenuItemViewModel() {
                    Header = ThemeManager.GetString("editbar.editbar"),
                    Command = BarsEditCommand,
                    CommandParameter = cur_tick,
                });

                shouldOpenNotesContextMenu = true;
                return;
            }

            LyricBox?.EndEdit();
        }

        public void TimelinePointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            lastPoint = point.Position;
            if (point.Properties.IsLeftButtonPressed) {
                ViewModel.NotesViewModel.PointToLineTick(point.Position, out int left, out int right);
                int tick = left + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
                return;
            }
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            var remarkHitInfo = ViewModel.NotesViewModel.HitTest.HitTestRemark(point.Position);
            if (remarkHitInfo.OnPoint) {
                if (!_remarkTimer.IsEnabled) {
                    _remarkTimer.Start();
                }
            } else {
                ((IValueTip)this).HideValueTip();
                if (_remarkTimer.IsEnabled) {
                    _remarkTimer.Stop();
                }
            }
        }

        public void TimelinePointerReleased(object sender, PointerReleasedEventArgs args) {
            args.Pointer.Capture(null);
        }
        public void TimeDoubleTapped(object sender, TappedEventArgs args) {
            if (!(sender is Control control)) {
                return;
            }
            var element = (TextBlock)sender;
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            var dialog = new TimeInDialog() {
                Title = ThemeManager.GetString("pianoroll.toggle.seekto"),
                onFinish = value => {

                    double milliseconds = -1;
                    try {
                        string timeString = value.Replace("：", ":");
                        string[] parts = timeString.Split(':');
                        int hours = 0;
                        int minutes = 0;
                        double seconds = 0;

                        if (parts.Length == 3) {
                            hours = int.Parse(parts[0]);
                            minutes = int.Parse(parts[1]);
                            seconds = double.Parse(parts[2]);
                            
                        } else if (parts.Length == 2) {
                            minutes = int.Parse(parts[0]);
                            seconds = double.Parse(parts[1]);
                        }
                        milliseconds = (hours * 3600 + minutes * 60 + seconds) * 1000;
                    } catch (Exception) {
                        milliseconds = -1;
                    }

                    if(milliseconds >=0) {
                        var tick = notesVM.Project.timeAxis.MsPosToTickPos(milliseconds);
                        if (tick < notesVM.Part.End) {
                            var realTick = tick - notesVM.Part.position;
                            ViewModel.PlaybackViewModel?.MovePlayPos(tick);
                            notesVM.TickOffset = realTick - notesVM.ViewportTicks / 2;
                            notesVM.PlayPosHighlightX = notesVM.TickToneToPoint(realTick, 0).X;
                        }
                    }
                }
            };
            if(element.Text != null) {
                dialog.SetText(element.Text);
            }
            else {
                dialog.SetText("00:00.000");
            }
            dialog.ShowDialog(this);
        }
        private void HandleShowRemark(object? sender, EventArgs e) {
            var remarkHitInfo = ViewModel.NotesViewModel.HitTest.HitTestRemark(lastPoint);
            if (remarkHitInfo.OnPoint) {
                ((IValueTip)this).ShowValueTip();
                ((IValueTip)this).UpdateValueTip(remarkHitInfo.Remark.text);
            } else {
                ((IValueTip)this).HideValueTip();
                if (_remarkTimer.IsEnabled) {
                    _remarkTimer.Stop();
                }
            }
        }
        public void RemarkContextMenuOpening(object sender, CancelEventArgs args) {
            if (shouldOpenNotesContextMenu) {
                shouldOpenNotesContextMenu = false;
            } else {
                args.Cancel = true;
            }
        }
        public void RemarkContextMenuClosing(object sender, CancelEventArgs args) {
            ViewModel.RemarkContextMenuItems?.Clear();
        }
        public void NotesCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                NotesCanvasLeftPointerPressed(control, point, args);
            } else if (point.Properties.IsRightButtonPressed) {
                NotesCanvasRightPointerPressed(control, point, args);
            } else if (point.Properties.IsMiddleButtonPressed) {
                editState = new NotePanningState(control, ViewModel, this);
                Cursor = ViewConstants.cursorHand;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        private void NotesCanvasLeftPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {
            if (ViewModel.NotesViewModel.DrawPitchTool || ViewModel.NotesViewModel.DrawLinePitchTool || ViewModel.NotesViewModel.OverwritePitchTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                if (args.KeyModifiers == KeyModifiers.Alt) {
                    editState = new SmoothenPitchState(control, ViewModel, this);
                    return;
                } else if (args.KeyModifiers != cmdKey) {
                    if (ViewModel.NotesViewModel.DrawPitchTool) {
                        editState = new DrawPitchState(control, ViewModel, this);
                    } else if (ViewModel.NotesViewModel.DrawLinePitchTool) {
                        editState = new DrawLinePitchState(control, ViewModel, this);
                    } else {
                        editState = new OverwritePitchState(control, ViewModel, this);
                    }
                    return;
                }
            }
            if (ViewModel.NotesViewModel.EraserTool && args.KeyModifiers != cmdKey) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(control, ViewModel, this, MouseButton.Left);
                Cursor = ViewConstants.cursorNo;
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null && !IsLockedEdit(ViewModel.LockPitchPoints, pitHitInfo.Note)) {
                editState = new PitchPointEditState(control, ViewModel, this,
                    pitHitInfo.Note, pitHitInfo.Index, pitHitInfo.OnPoint, pitHitInfo.X, pitHitInfo.Y);
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit && !IsLockedEdit(ViewModel.LockVibrato, vbrHitInfo.note)) {
                if (vbrHitInfo.hitToggle) {
                    ViewModel.NotesViewModel.ToggleVibrato(vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitStart) {
                    editState = new VibratoChangeStartState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitIn) {
                    editState = new VibratoChangeInState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitOut) {
                    editState = new VibratoChangeOutState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitDepth) {
                    editState = new VibratoChangeDepthState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitPeriod) {
                    editState = new VibratoChangePeriodState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitShift) {
                    editState = new VibratoChangeShiftState(
                        control, ViewModel, this, vbrHitInfo.note, vbrHitInfo.point, vbrHitInfo.initialShift);
                    return;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitBody) {
                if (noteHitInfo.hitResizeArea) {
                    editState = new NoteResizeEditState(
                        control, ViewModel, this, noteHitInfo.note,
                        args.KeyModifiers == KeyModifiers.Alt,
                        fromStart: noteHitInfo.hitResizeAreaFromStart);
                    Cursor = ViewConstants.cursorSizeWE;
                } else if (args.KeyModifiers == cmdKey) {
                    ViewModel.NotesViewModel.ToggleSelectNote(noteHitInfo.note);
                } else if (args.KeyModifiers == KeyModifiers.Shift) {
                    ViewModel.NotesViewModel.SelectNotesUntil(noteHitInfo.note);
                } else if (ViewModel.NotesViewModel.KnifeTool) {
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSplitEditState(
                            control, ViewModel, this, noteHitInfo.note);
                } else {
                    editState = new NoteMoveEditState(control, ViewModel, this, noteHitInfo.note);
                    Cursor = ViewConstants.cursorSizeAll;
                }
                return;
            }
            if (ViewModel.NotesViewModel.CursorTool || args.KeyModifiers == cmdKey) {
                if (args.KeyModifiers == KeyModifiers.None) {
                    // New selection.
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSelectionEditState(control, ViewModel, this, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                if (args.KeyModifiers == cmdKey) {
                    // Additional selection.
                    editState = new NoteSelectionEditState(control, ViewModel, this, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                ViewModel.NotesViewModel.DeselectNotes();
            } else if (ViewModel.NotesViewModel.PenTool ||
                ViewModel.NotesViewModel.PenPlusTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteDrawEditState(control, ViewModel, this, ViewModel.NotesViewModel.PlayTone);
            }
        }

        private void NotesCanvasRightPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {
            var selectedNotes = ViewModel.NotesViewModel.Selection.ToList();
            if (ViewModel.NotesViewModel.DrawPitchTool || ViewModel.NotesViewModel.DrawLinePitchTool || ViewModel.NotesViewModel.OverwritePitchTool) {
                editState = new ResetPitchState(control, ViewModel, this);
                return;
            }
            if (ViewModel.NotesViewModel.ShowPitch) {
                var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
                if (pitHitInfo.Note != null && !IsLockedEdit(ViewModel.LockPitchPoints, pitHitInfo.Note)) {
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.easeinout"),
                        Command = ViewModel.PitEaseInOutCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.linear"),
                        Command = ViewModel.PitLinearCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.easein"),
                        Command = ViewModel.PitEaseInCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.easeout"),
                        Command = ViewModel.PitEaseOutCommand,
                        CommandParameter = pitHitInfo,
                    });
                    if (pitHitInfo.OnPoint && pitHitInfo.Index == 0) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointsnapprev"),
                            Command = ViewModel.PitSnapCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    if (pitHitInfo.OnPoint && pitHitInfo.Index != 0 &&
                        pitHitInfo.Index != pitHitInfo.Note.pitch.data.Count - 1) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointdel"),
                            Command = ViewModel.PitDelCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    if (!pitHitInfo.OnPoint) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointadd"),
                            Command = ViewModel.PitAddCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    shouldOpenNotesContextMenu = true;
                    return;
                }
            }
            if (ViewModel.NotesViewModel.CursorTool || ViewModel.NotesViewModel.PenTool || ViewModel.NotesViewModel.KnifeTool) {
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
                var vibHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
                if ((hitInfo.hitBody && hitInfo.note != null) || vibHitInfo.hit) {
                    if (hitInfo.note != null && !selectedNotes.Contains(hitInfo.note)) {
                        ViewModel.NotesViewModel.DeselectNotes();
                        ViewModel.NotesViewModel.SelectNote(hitInfo.note, false);
                    }
                    if (ViewModel.NotesViewModel.Selection.Count > 0) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.copy"),
                            Command = ViewModel.NoteCopyCommand,
                            CommandParameter = hitInfo,
                            InputGesture = new KeyGesture(Key.C, KeyModifiers.Control),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.delete"),
                            Command = ViewModel.NoteDeleteCommand,
                            CommandParameter = hitInfo,
                            InputGesture = new KeyGesture(Key.Delete),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.pasteparameters"),
                            Command = ReactiveCommand.Create(() => ViewModel.NotesViewModel.PasteSelectedParams(this)),
                            InputGesture = new KeyGesture(Key.V, KeyModifiers.Alt),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.notes"),
                            Items = ViewModel.NoteBatchEdits.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.lyrics"),
                            Items = ViewModel.LyricBatchEdits.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.lyrics.edit"),
                            Command = lyricsDialogCommand,
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.notedefaults"),
                            Command = noteDefaultsCommand,
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.clearcache"),
                            Command = ViewModel.ClearPhraseCacheCommand,
                        });
                        if (selectedNotes.Count == 2) {
                            if (selectedNotes[0].tone == selectedNotes[1].tone && 
                                (selectedNotes[0].End == selectedNotes[1].position || 
                                selectedNotes[1].End == selectedNotes[0].position)) {
                                ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                                    Header = ThemeManager.GetString("pianoroll.menu.notes.mergenotes"),
                                    Command = ViewModel.MergeNotesCommand,
                                    CommandParameter = selectedNotes,
                                });
                            }
                        }
                        shouldOpenNotesContextMenu = true;
                        return;
                    }
                } else {
                    ViewModel.NotesViewModel.DeselectNotes();
                }
            } else if (ViewModel.NotesViewModel.EraserTool || ViewModel.NotesViewModel.PenPlusTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(control, ViewModel, this, MouseButton.Right);
                Cursor = ViewConstants.cursorNo;
            }
        }

        public void NotesCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            if (ViewModel?.NotesViewModel?.HitTest == null) {
                return;
            }
            if (((ViewModel.NotesViewModel.DrawPitchTool || ViewModel.NotesViewModel.DrawLinePitchTool || ViewModel.NotesViewModel.OverwritePitchTool) && args.KeyModifiers != cmdKey) || ViewModel.NotesViewModel.EraserTool) {
                Cursor = null;
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null && !IsLockedEdit(ViewModel.LockPitchPoints, pitHitInfo.Note)) {
                Cursor = ViewConstants.cursorHand;
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit && !IsLockedEdit(ViewModel.LockVibrato, vbrHitInfo.note)) {
                if (vbrHitInfo.hitDepth) {
                    Cursor = ViewConstants.cursorSizeNS;
                } else if (vbrHitInfo.hitPeriod) {
                    Cursor = ViewConstants.cursorSizeWE;
                } else {
                    Cursor = ViewConstants.cursorHand;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitResizeArea) {
                Cursor = ViewConstants.cursorSizeWE;
                return;
            }
            Cursor = null;
        }

        public void NotesCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if(editState is not SmoothenPitchState)
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void NotesCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (!(sender is Control control)) {
                return;
            }
            var point = args.GetPosition(control);
            if (editState != null) {
                editState.End(args.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point);
            if (noteHitInfo.hitBody && ViewModel?.NotesViewModel?.Part != null) {
                var note = noteHitInfo.note;
                LyricBox?.Show(ViewModel.NotesViewModel.Part, new LyricBoxNote(note), note.lyric);
            }
        }

        public void NotesCanvasPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            var control = (Control)sender;
            var position = args.GetCurrentPoint(control).Position;
            var size = control.Bounds.Size;
            var delta = args.Delta;
            if (args.KeyModifiers == (cmdKey | KeyModifiers.Shift) && ViewModel.NotesViewModel.ShowWaveform) {
                if (delta.Y > 0) {
                    ViewModel.NotesViewModel.WavShapeBorderHeight = Math.Clamp(ViewModel.NotesViewModel.WavShapeBorderHeight + 10, 60, 300);
                } else if (delta.Y < 0) {
                    ViewModel.NotesViewModel.WavShapeBorderHeight = Math.Clamp(ViewModel.NotesViewModel.WavShapeBorderHeight - 10, 60, 300);
                }
            }
            else if(args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (args.KeyModifiers == KeyModifiers.Shift) {
                    delta = new Vector(delta.Y, delta.X);
                }
                if (delta.X != 0) {
                    HScrollBar.Value = Math.Max(HScrollBar.Minimum,
                        Math.Min(HScrollBar.Maximum, HScrollBar.Value - HScrollBar.SmallChange * delta.X));
                }
                if (delta.Y != 0) {
                    VScrollBar.Value = Math.Max(VScrollBar.Minimum,
                        Math.Min(VScrollBar.Maximum, VScrollBar.Value - VScrollBar.SmallChange * delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
                ViewModel.NotesViewModel.OnYZoomed(position, 0.1 * args.Delta.Y);
            } else if (args.KeyModifiers == cmdKey) {
                TimelinePointerWheelChanged(TimelineCanvas, args);
            }
            if (editState != null) {
                var point = args.GetCurrentPoint(editState.control);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void NotesContextMenuOpening(object sender, CancelEventArgs args) {
            if (shouldOpenNotesContextMenu) {
                shouldOpenNotesContextMenu = false;
            } else {
                args.Cancel = true;
            }
        }

        public void NotesContextMenuClosing(object sender, CancelEventArgs args) {
            ViewModel.NotesContextMenuItems?.Clear();
        }

        public void ExpCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                if(ViewModel.NotesViewModel.IsEditCurve) {
                    ExpCanvasLeftPointerPressed(control, point, args);
                } else {
                editState = new ExpSetValueState(control, ViewModel, this);
                }
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new ExpResetValueState(control, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            } else if (point.Properties.IsMiddleButtonPressed) {
                editState = new ExpCurvePanningState(control, ViewModel, this);
                Cursor = ViewConstants.cursorHand;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position, args);
            }
        }
        private void ExpCanvasLeftPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {

            var curveHitInfo = ViewModel.NotesViewModel.HitTest.HitTestCurvePoint(point.Position, control.Bounds.Height);
            if (curveHitInfo.OnPoint) {
                if (args.KeyModifiers == cmdKey) {
                    ViewModel.NotesViewModel.ToggleSelectCurvePoint(curveHitInfo.Index);
                } else if (args.KeyModifiers == KeyModifiers.Shift) {
                    ViewModel.NotesViewModel.SelectCurvePointsUntil(curveHitInfo.Index);
                } else {
                    if (!ViewModel.NotesViewModel.SelectionPoints.Contains(curveHitInfo.Index)) {
                        ViewModel.NotesViewModel.SelectCurvePoints(new List<int> { curveHitInfo.Index });
                    }

                    editState = new ExpPointsMoveEditState(control, ViewModel, this);
                    ViewModel.NotesViewModel.LastSelectionPoint = curveHitInfo.Index;
                }
            } else {
                if(args.KeyModifiers == cmdKey) {
                    ViewModel.NotesViewModel.setTmpSelectCurvePoints();
                } else {
                    ViewModel.NotesViewModel.DeselectCurvePoints();
                    ViewModel.NotesViewModel.LastSelectionPos = point.Position;
                }
                editState = new ExpPointsSelectionEditState(control, ViewModel, this, PointsSelectionBox);
                Cursor = ViewConstants.cursorCross;

                ViewModel.NotesViewModel.LastSelectionPoint = -1;
            }
        }
        public void ExpCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.Update(point.Pointer, point.Position, args);
            } else {
                if (ViewModel.NotesViewModel.IsEditCurve) {
                    var curveHitInfo = ViewModel.NotesViewModel.HitTest.HitTestCurvePoint(point.Position, control.Bounds.Height);
                    if (curveHitInfo.OnPoint) {
                        Cursor = ViewConstants.cursorHand;
                    } else {
                Cursor = null;
            }
                } else {
                    Cursor = null;
                }
            }
        }

        public void ExpCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.Update(point.Pointer, point.Position, args);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }
        public void ExpCanvasPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            var control = (Control)sender;
            var position = args.GetCurrentPoint(control).Position;
            var size = control.Bounds.Size;
            var delta = args.Delta;
            if (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (args.KeyModifiers == KeyModifiers.Shift) {
                    delta = new Vector(delta.Y, delta.X);
                }
                if (delta.X != 0) {
                    HScrollBar.Value = Math.Max(HScrollBar.Minimum,
                        Math.Min(HScrollBar.Maximum, HScrollBar.Value - HScrollBar.SmallChange * delta.X));
                }
                if (delta.Y != 0) {
                    VScrollBar.Value = Math.Max(VScrollBar.Minimum,
                        Math.Min(VScrollBar.Maximum, VScrollBar.Value - VScrollBar.SmallChange * delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
                ViewModel.NotesViewModel.OnYZoomed(position, 0.1 * args.Delta.Y);
            } else if (args.KeyModifiers == cmdKey) {
                TimelinePointerWheelChanged(TimelineCanvas, args);
            }
            if (editState != null) {
                var point = args.GetCurrentPoint(editState.control);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void PhonemeCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (sender is not Control control) {
                return;
            }
            var point = args.GetPosition(control);
            if (editState != null) {
                editState.End(args.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(point);
            var phoneme = hitInfo.phoneme;
            Log.Debug($"PhonemeCanvasDoubleTapped, hit = {hitInfo.hit}, point = {{{hitInfo.point}}}, phoneme = {phoneme?.phoneme}");
            if (!hitInfo.hit) {
                return;
            }
            LyricBox?.Show(ViewModel.NotesViewModel.Part, new LyricBoxPhoneme(phoneme!), phoneme!.phoneme);
        }

        public void PhonemeCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                if (args.KeyModifiers == cmdKey) {
                    var hitAliasInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(args.GetPosition(control));
                    if (hitAliasInfo.hit) {
                        var singer = ViewModel.NotesViewModel.Project.tracks[ViewModel.NotesViewModel.Part.trackNo].Singer;
                        if (Preferences.Default.OtoEditor == 1 && !string.IsNullOrEmpty(Preferences.Default.VLabelerPath)) {
                            Integrations.VLabelerClient.Inst.GotoOto(singer, hitAliasInfo.phoneme.oto);
                        } else {
                            MainWindow?.OpenSingersWindow();
                            this.Activate();
                            DocManager.Inst.ExecuteCmd(new GotoOtoNotification(singer, hitAliasInfo.phoneme.oto));
                        }
                        return;
                    }
                }
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
                if (hitInfo.hit) {
                    var phoneme = hitInfo.phoneme;
                    var note = phoneme.Parent;
                    var index = phoneme.index;
                    if (hitInfo.hitPosition) {
                        editState = new PhonemeMoveState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitPreutter) {
                        editState = new PhonemeChangePreutterState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitOverlap) {
                        editState = new PhonemeChangeOverlapState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    }
                }
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new PhonemeResetState(control, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void PhonemeCanvasPointerMoved(object sender, PointerEventArgs args) {
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            var aliasHitInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(point.Position);
            if (aliasHitInfo.hit) {
                ViewModel.MouseoverPhoneme(aliasHitInfo.phoneme);
                Cursor = null;
                return;
            }
            var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
            if (hitInfo.hit) {
                Cursor = ViewConstants.cursorSizeWE;
                ViewModel.MouseoverPhoneme(null);
                return;
            }
            ViewModel.MouseoverPhoneme(null);
            Cursor = null;
        }

        public void PhonemeCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        private bool IsLockedEdit(bool locked, UNote note) {
            return locked && ViewModel.NotesViewModel.Selection.Count > 0 && !ViewModel.NotesViewModel.Selection.Contains(note);
        }

        public void OnSnapDivMenuButton(object sender, RoutedEventArgs args) {
            SnapDivMenu.PlacementTarget = sender as Button;
            SnapDivMenu.Open();
        }

        void OnSnapDivKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) {
                if (sender is ContextMenu menu && menu.SelectedItem is MenuItemViewModel item) {
                    item.Command?.Execute(item.CommandParameter);
                }
            }
        }

        public void OnKeyMenuButton(object sender, RoutedEventArgs args) {
            KeyMenu.PlacementTarget = sender as Button;
            KeyMenu.Open();
        }

        bool MoveToNextPart(bool next) {
            var notesVm = ViewModel.NotesViewModel;
            var playVm = ViewModel.PlaybackViewModel;
            if (notesVm?.Part == null || playVm == null) {
                return false;
            }
            // tick is the center of NotesCanvas
            var tick = (int)(notesVm.TickOffset + notesVm.Bounds.Width / notesVm.TickWidth / 2 + notesVm.Part.position);
            var parts = notesVm.Project.parts
                .Where(part => part is UVoicePart && part.position <= tick && tick <= part.End)
                .OfType<UVoicePart>()
                .OrderBy(part => part.trackNo)
                .ThenBy(part => part.position)
                .ToArray();
            if (parts.Length == 0) {
                return false;
            }
            var index = Array.IndexOf(parts, notesVm.Part);
            index = next ? index + 1 : index - 1;
            if (parts.Length <= index) {
                index = 0;
            } else if (index < 0) {
                index = parts.Length - 1;
            }
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(parts[index], notesVm.Project, tick));
            AttachExpressions();
            return true;
        }

        void OnKeyKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) {
                if (sender is ContextMenu menu && menu.SelectedItem is MenuItemViewModel item) {
                    item.Command?.Execute(item.CommandParameter);
                }
            }
        }

        #region value tip

        void IValueTip.ShowValueTip() {
            if (ValueTip != null) {
                ValueTip.IsVisible = true;
            }
        }

        void IValueTip.HideValueTip() {
            if (ValueTip != null) {
                ValueTip.IsVisible = false;
            }
            if (ValueTipText != null) {
                ValueTipText.Text = string.Empty;
            }
        }

        void IValueTip.UpdateValueTip(string text) {
            if (ValueTip == null || ValueTipText == null || ValueTipCanvas == null) {
                return;
            }
            ValueTipText.Text = text;
            Canvas.SetLeft(ValueTip, valueTipPointerPosition.X);
            double tipY = valueTipPointerPosition.Y + 21;
            if (tipY + 21 > ValueTipCanvas!.Bounds.Height) {
                tipY = tipY - 42;
            }
            Canvas.SetTop(ValueTip, tipY);
        }

        #endregion

        void OnKeyDown(object sender, KeyEventArgs args) {
            var notesVm = ViewModel.NotesViewModel;
            if (notesVm.Part == null) {
                args.Handled = false;
                return;
            }

            if (FocusManager != null && FocusManager.GetFocusedElement() is TextBox focusedTextBox) {
                if (focusedTextBox.IsEnabled && focusedTextBox.IsEffectivelyVisible && focusedTextBox.IsFocused) {
                    args.Handled = false;
                    return;
                }
            }

            // returns true if handled
            args.Handled = OnKeyExtendedHandler(args);
        }

        bool OnKeyExtendedHandler(KeyEventArgs args) {
            var notesVm = ViewModel.NotesViewModel;
            var playVm = ViewModel.PlaybackViewModel;
            if (notesVm?.Part == null || playVm == null) {
                return false;
            }
            var project = Core.DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int deltaTicks = notesVm.IsSnapOn ? snapUnit : 15;

            bool isNone = args.KeyModifiers == KeyModifiers.None;
            bool isAlt = args.KeyModifiers == KeyModifiers.Alt;
            bool isCtrl = args.KeyModifiers == cmdKey;
            bool isShift = args.KeyModifiers == KeyModifiers.Shift;
            bool isBoth = args.KeyModifiers == (cmdKey | KeyModifiers.Shift);
            bool isCtrlAlt = args.KeyModifiers == (cmdKey | KeyModifiers.Alt);
            bool isShiftAlt = args.KeyModifiers == (KeyModifiers.Shift | KeyModifiers.Alt);

            if (PluginMenu.IsSubMenuOpen && isNone) {
                if (ViewModel.LegacyPluginShortcuts.ContainsKey(args.Key)) {
                    var plugin = ViewModel.LegacyPluginShortcuts[args.Key];
                    if (plugin != null && plugin.Command != null) {
                        plugin.Command.Execute(plugin.CommandParameter);
                    }
                }
                return true;
            }

            foreach (MenuItemViewModel menu in ViewModel.NoteBatchEdits) {
                if(menu.InputGesture != null) {
                    if (menu.InputGesture.Matches(args)) {
                        menu?.Command?.Execute(menu.CommandParameter);
                        return true;
                    }
                }
            }

            foreach (MenuItemViewModel menu in ViewModel.LyricBatchEdits) {
                if (menu.InputGesture != null) {
                    if (menu.InputGesture.Matches(args)) {
                        menu?.Command?.Execute(menu.CommandParameter);
                        return true;
                    }
                }
            }
            foreach (MenuItemViewModel menu in ViewModel.ResetBatchEdits) {
                if (menu.InputGesture != null) {
                    if (menu.InputGesture.Matches(args)) {
                        menu?.Command?.Execute(menu.CommandParameter);
                        return true;
                    }
                }
            }

            string mainPenIdx = Preferences.Default.PenPlusDefault ? "2+" : "2";
            string altPenIdx = Preferences.Default.PenPlusDefault ? "2" : "2+";

            switch (args.Key) {
                #region document keys
                case Key.Space:
                    if (isNone) {
                        playVm.PlayOrPause();
                        
                        return true;
                    }
                    if (isAlt) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.PlayOrPause(
                                tick: notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position,
                                endTick: notesVm.Part.position + notesVm.Selection.LastOrDefault()!.RightBound
                            );
                        }
                        return true;
                    }
                    break;
                case Key.Escape:
                    if (isNone) {
                        // collapse/empty selection
                        var numSelected = notesVm.Selection.Count;
                        // if single or all notes then clear
                        if (numSelected == 1 || numSelected == notesVm.Part.notes.Count) {
                            notesVm.DeselectNotes();
                        } else if (numSelected > 1) {
                            // collapse selection
                            notesVm.SelectNote(notesVm.Selection.Head!);
                        }
                        return true;
                    }
                    break;
                case Key.F4:
                    if (isAlt) {
                        Hide();
                        return true;
                    }
                    break;
                case Key.F11:
                    OnMenuFullScreen(this, new RoutedEventArgs());
                    break;
                case Key.Enter:
                    if (isNone) {
                        if (notesVm.Selection.Count == 1) {
                            var note = notesVm.Selection.First();
                            LyricBox?.Show(ViewModel.NotesViewModel.Part!, new LyricBoxNote(note), note.lyric);
                        } else if (notesVm.Selection.Count > 1) {
                            EditLyrics();
                        }
                        return true;
                    }
                    break;
                #endregion
                #region tool select keys
                // TOOL SELECT
                case Key.D1:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("1").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector1?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D2:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute(mainPenIdx).Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector2?.SelectExp();
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.SelectToolCommand?.Execute(altPenIdx).Subscribe();
                        return true;
                    }
                    break;
                case Key.D3:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("3").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector3?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D4:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("4").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector4?.SelectExp();
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.SelectToolCommand?.Execute("4+").Subscribe();
                        return true;
                    }
                    if (isShift) {
                        notesVm.SelectToolCommand?.Execute("4++").Subscribe();
                        return true;
                    }
                    break;
                case Key.D5:
                    if (isNone) {
                        notesVm.SelectToolCommand?.Execute("5").Subscribe();
                        return true;
                    }
                    if (isAlt) {
                        expSelector5?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D6:
                    if (isAlt) {
                        expSelector6?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D7:
                    if (isAlt) {
                        expSelector7?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D8:
                    if (isAlt) {
                        expSelector8?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D9:
                    if (isAlt) {
                        expSelector9?.SelectExp();
                        return true;
                    }
                    break;
                case Key.D0:
                    if (isAlt) {
                        expSelector10?.SelectExp();
                        return true;
                    }
                    break;
                #endregion
                #region toggle show keyws
                case Key.R:
                    if (isNone) {
                        notesVm.ShowFinalPitch = !notesVm.ShowFinalPitch;
                        return true;
                    }
                    break;
                case Key.T:
                    if (isNone) {
                        notesVm.ShowTips = !notesVm.ShowTips;
                        return true;
                    }
                    if (isAlt) {
                        EditLyrics(); ;
                        return true;
                    }
                    break;
                case Key.U:
                    if (isNone) {
                        notesVm.ShowVibrato = !notesVm.ShowVibrato;
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.MergeSelectedNotes();
                        return true;
                    }
                    break;
                case Key.I:
                    if (isNone) {
                        notesVm.ShowPitch = !notesVm.ShowPitch;
                        return true;
                    }
                    break;
                case Key.O:
                    if (isNone) {
                        notesVm.ShowPhoneme = !notesVm.ShowPhoneme;
                        return true;
                    }
                    break;
                case Key.P:
                    if (isNone) {
                        notesVm.IsSnapOn = !notesVm.IsSnapOn;
                        return true;
                    }
                    if (isAlt) {
                        SnapDivMenu.Open();
                    }
                    break;
                case Key.OemPipe:
                    if (isNone) {
                        notesVm.ShowNoteParams = !notesVm.ShowNoteParams;
                        return true;
                    }
                    break;
                #endregion
                #region navigate keys
                // NAVIGATE/EDIT/SELECT HANDLERS
                case Key.Up:
                    if (isNone) {
                        notesVm.TransposeSelection(1);
                        return true;
                    }
                    if(isCtrlAlt) {
                        notesVm.MoveSelectionCurvePoints(0, 10);
                    }
                    if(isShiftAlt) {
                        notesVm.MoveSelectionCurvePoints(0, 1);
                    }
                    if (isCtrl) {
                        notesVm.TransposeSelection(12);
                        return true;
                    }
                    if(isAlt) {
                        MetronomePlayer.Instance.Volume += 0.1f;
                    }
                    break;
                case Key.Down:
                    if (isNone) {
                        notesVm.TransposeSelection(-1);
                        return true;
                    }
                    if (isCtrlAlt) {
                        notesVm.MoveSelectionCurvePoints(0, -10);
                    }
                    if (isShiftAlt) {
                        notesVm.MoveSelectionCurvePoints(0, -1);
                    }
                    if (isCtrl) {
                        notesVm.TransposeSelection(-12);
                        return true;
                    }
                    if (isAlt) {
                        MetronomePlayer.Instance.Volume -= 0.1f;
                    }
                    break;
                case Key.Left:
                    if (isNone) {
                        notesVm.MoveCursor(-1);
                        return true;
                    }
                    if (isCtrlAlt) {
                        notesVm.MoveSelectionCurvePoints(-50, 0);
                    }
                    if (isShiftAlt) {
                        notesVm.MoveSelectionCurvePoints(-5, 0);
                    }
                    if (isAlt) {
                        notesVm.ResizeSelectedNotes(-1 * deltaTicks);
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.MoveSelectedNotes(-1 * deltaTicks);
                        return true;
                    }
                    if (isShift) {
                        notesVm.ExtendSelection(-1);
                        return true;
                    }
                    break;
                case Key.Right:
                    if (isNone) {
                        notesVm.MoveCursor(1);
                        return true;
                    }
                    if (isCtrlAlt) {
                        notesVm.MoveSelectionCurvePoints(50, 0);
                    }
                    if (isShiftAlt) {
                        notesVm.MoveSelectionCurvePoints(5, 0);
                    }
                    if (isAlt) {
                        notesVm.ResizeSelectedNotes(deltaTicks);
                        return true;
                    }
                    if (isCtrl) {
                        notesVm.MoveSelectedNotes(deltaTicks);
                        return true;
                    }
                    if (isShift) {
                        notesVm.ExtendSelection(1);
                        return true;
                    }
                    break;
                case Key.OemPlus:
                    if (isNone) {
                        notesVm.ResizeSelectedNotes(deltaTicks);
                        return true;
                    }
                    break;
                case Key.OemMinus:
                    if (isNone) {
                        notesVm.ResizeSelectedNotes(-1 * deltaTicks);
                        return true;
                    }
                    break;
                #endregion
                #region clipboard and edit keys
                case Key.Z:
                    if (isBoth) {
                        ViewModel.Redo();
                        return true;
                    }
                    if (isCtrl) {
                        ViewModel.Undo();
                        return true;
                    }
                    break;
                case Key.Y:
                    // toggle play tone
                    if (isNone) {
                        notesVm.PlayTone = !notesVm.PlayTone;
                        return true;
                    }
                    if (isCtrl) {
                        ViewModel.Redo();
                        return true;
                    }
                    break;
                case Key.C:
                    if (isCtrl) {
                        notesVm.CopyNotes();
                        return true;
                    }
                    if(isShift) {
                        notesVm.CopyCurvePoints();
                        return true;
                    }
                    break;
                case Key.X:
                    if (isCtrl) {
                        notesVm.CutNotes();
                        return true;
                    }
                    break;
                case Key.V:
                    if (isCtrl) {
                        notesVm.PasteNotes();
                        return true;
                    }
                    if (isAlt) {
                        notesVm.PasteSelectedParams(this);
                        return true;
                    }
                    if(isShift) {
                        notesVm.PasteCurvePoints();
                        return true;
                    }
                    break;
                case Key.N:
                    if (isNone && PluginMenu.Parent is MenuItem batch) {
                        batch.Open();
                        PluginMenu.Open();
                        return true;
                    }
                    break;
                // INSERT + DELETE
                case Key.Insert:
                    if (isNone) {
                        notesVm.InsertNote();
                        return true;
                    }
                    break;
                case Key.Delete:
                case Key.Back:
                    if (isNone) {
                        notesVm.DeleteSelectedNotes();
                        return true;
                    }
                    if(isShift) {
                        notesVm.DeleteCurvePoints();
                        return true;
                    }
                    break;
                #endregion
                #region play position and select keys
                // PLAY POSITION + SELECTION
                case Key.Home:
                    if (isNone) {
                        playVm.MovePlayPos(notesVm.Part.position);
                        return true;
                    }
                    if (isShift) {
                        var first = notesVm.Part.notes.FirstOrDefault();
                        if (first != null) {
                            notesVm.ExtendSelection(first);
                        }
                        return true;
                    }
                    break;
                case Key.End:
                    if (isNone) {
                        playVm.MovePlayPos(notesVm.Part.End);
                        HScrollBar.Value = HScrollBar.Maximum;
                        return true;
                    }
                    if (isShift) {
                        var last = notesVm.Part.notes.LastOrDefault();
                        if (last != null) {
                            notesVm.ExtendSelection(last);
                        }
                        return true;
                    }
                    break;
                case Key.OemOpenBrackets:
                    // move playhead left
                    if (isNone) {
                        playVm.MovePlayPos(playVm.PlayPosTick - snapUnit);
                        return true;
                    }
                    // to selection start
                    if (isCtrl) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position);
                        }
                        return true;
                    }
                    // to view start
                    if (isShift) {
                        playVm.MovePlayPos(notesVm.Part.position + (int)notesVm.TickOffset);
                        return true;
                    }
                    break;
                case Key.OemCloseBrackets:
                    // move playhead right
                    if (isNone) {
                        playVm.MovePlayPos(playVm.PlayPosTick + snapUnit);
                        return true;
                    }
                    // to selection end
                    if (isCtrl) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.LastOrDefault()!.RightBound);
                        }
                        return true;
                    }
                    // to view end
                    if (isShift) {
                        playVm.MovePlayPos(notesVm.Part.position + (int)(notesVm.TickOffset + notesVm.Bounds.Width / notesVm.TickWidth));
                        return true;
                    }
                    break;

                #endregion
                #region scroll and select keys
                // SCROLL / SELECT
                case Key.A:
                    // scroll left
                    if (isNone) {
                        notesVm.TickOffset = Math.Max(0, notesVm.TickOffset - snapUnit);
                        return true;
                    }
                    // select all
                    if (isCtrl) {
                        notesVm.SelectAllNotes();
                        return true;
                    }
                    break;
                case Key.D:
                    // scroll right
                    if (isNone) {
                        notesVm.TickOffset = Math.Min(notesVm.TickOffset + snapUnit, notesVm.HScrollBarMax);
                        return true;
                    }
                    // select none
                    if (isCtrl) {
                        notesVm.DeselectNotes();
                        return true;
                    }
                    break;
                case Key.W:
                    // toggle show waveform
                    if (isNone) {
                        notesVm.ShowWaveform = !notesVm.ShowWaveform;
                        return true;
                    }
                    // scroll up
                    // NOTE set to alt to avoid conflict with showwaveform toggle
                    if (isAlt) {
                        notesVm.TrackOffset = Math.Max(notesVm.TrackOffset - 2, 0);
                        return true;
                    }
                    break;
                case Key.S:
                    // scroll down
                    if (isAlt) {
                        notesVm.TrackOffset = Math.Min(notesVm.TrackOffset + 2, notesVm.VScrollBarMax);
                        return true;
                    }
                    if (isCtrl) {
                        _ = MainWindow?.Save();
                        return true;
                    }
                    // solo
                    if (isShift) {
                        var track = project.tracks[notesVm.Part.trackNo];
                        MessageBus.Current.SendMessage(new TracksSoloEvent(notesVm.Part.trackNo, !track.Solo, false));
                        return true;
                    }
                    break;
                case Key.M:
                    // mute
                    if (isShift) {
                        MessageBus.Current.SendMessage(new TracksMuteEvent(notesVm.Part.trackNo, false));
                    }
                    break;
                case Key.F:
                    // scroll selection into focus
                    if (isNone) {
                        var note = notesVm.Selection.FirstOrDefault();
                        if (note != null) {
                            DocManager.Inst.ExecuteCmd(new FocusNoteNotification(notesVm.Part, note));
                        }
                        return true;
                    }
                    if (isCtrl) {
                        SearchNote();
                        return true;
                    }
                    if (isAlt) {
                        if (!notesVm.Selection.IsEmpty) {
                            playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position);
                        }
                        return true;
                    }
                    break;
                case Key.E:
                    // zoom in
                    if (isNone) {
                        double x = 0;
                        double y = 0;
                        if (!notesVm.Selection.IsEmpty) {
                            x = (notesVm.Selection.Head!.position - notesVm.TickOffset) / notesVm.ViewportTicks;
                            y = (ViewConstants.MaxTone - 1 - notesVm.Selection.Head.tone - notesVm.TrackOffset) / notesVm.ViewportTracks;
                        } else if (notesVm.TickOffset != 0) {
                            x = 0.5;
                            y = 0.5;
                        }
                        notesVm.OnXZoomed(new Point(x, y), 0.1);
                        return true;
                    }
                    if(isCtrl) {
                        notesVm.IsEditCurve = !notesVm.IsEditCurve;
                        MessageBus.Current.SendMessage(new CurvesRefreshEvent());
                        return true;
                    }
                    break;
                case Key.Q:
                    // zoom out
                    if (isNone) {
                        double x = 0;
                        double y = 0;
                        if (!notesVm.Selection.IsEmpty) {
                            x = (notesVm.Selection.Head!.position - notesVm.TickOffset) / notesVm.ViewportTicks;
                            y = (ViewConstants.MaxTone - 1 - notesVm.Selection.Head.tone - notesVm.TrackOffset) / notesVm.ViewportTracks;
                        } else if (notesVm.TickOffset != 0) {
                            x = 0.5;
                            y = 0.5;
                        }
                        notesVm.OnXZoomed(new Point(x, y), -0.1);
                        return true;
                    }
                    break;
                    #endregion
                #region move to the next track part
                case Key.PageUp: {
                        if (isNone) {
                            return MoveToNextPart(false);
                        }
                    }
                    break;
                    case Key.PageDown: {
                        if (isNone) {
                            return MoveToNextPart(true);
                        }
                    }
                    break;
                #endregion
            }
            return false;
        }

        public void AttachExpressions() {
            if (expSelector1 == null) {
                return;
            }
            var exps = new ExpSelector[] { expSelector1, expSelector2, expSelector3, expSelector4, expSelector5, expSelector6, expSelector7, expSelector8, expSelector9, expSelector10 };
            exps[DocManager.Inst.Project.expSecondary].SelectExp();
            exps[DocManager.Inst.Project.expPrimary].SelectExp();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadingNotification loadingNotif && loadingNotif.window == typeof(PianoRollWindow)) {
                if (loadingNotif.startLoading) {
                    MessageBox.ShowLoading(this);
                } else {
                    MessageBox.CloseLoading();
                }
            }
        }
    }
}
