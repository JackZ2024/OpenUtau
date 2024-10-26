using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class TrackHeaderCanvas : Canvas {
        public static readonly DirectProperty<TrackHeaderCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackHeaderCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackHeaderCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackHeaderCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackHeaderCanvas, ObservableCollection<UTrack>> ItemsProperty =
            AvaloniaProperty.RegisterDirect<TrackHeaderCanvas, ObservableCollection<UTrack>>(
                nameof(Items),
                o => o.Items,
                (o, v) => o.Items = v);

        public double TrackHeight {
            get => trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double TrackOffset {
            get => trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref trackOffset, value);
        }
        public ObservableCollection<UTrack> Items {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }

        private double trackHeight;
        private double trackOffset;
        private ObservableCollection<UTrack> _items = new ObservableCollection<UTrack>();

        private Dictionary<UTrack, TrackHeader> trackHeaders = new Dictionary<UTrack, TrackHeader>();
        private TrackAdder? trackAdder;

        private TrackHeader? curTrackHeader = null;
        private TrackHeader? lastTrackHeader = null;
        private UTrack? dragTrack = null;

        public TrackHeaderCanvas() {
            MessageBus.Current.Listen<TracksRefreshEvent>()
                .Subscribe(_ => {
                    foreach (var (track, header) in trackHeaders) {
                        if (header.ViewModel == null) {
                            continue;
                        }
                        header.ViewModel.JudgeMuted();
                        header.ViewModel.ManuallyRaise();
                    }
                    if (trackAdder != null) {
                        trackAdder.TrackNo = trackHeaders.Count;
                    }
                });
            MessageBus.Current.Listen<TracksSoloEvent>()
                .Subscribe(e => {
                    foreach (var (track, header) in trackHeaders) {
                        if (header.ViewModel != null) {
                            if (e.solo) {
                                if (track.TrackNo == e.trackNo) {
                                    header.ViewModel.Solo = true;
                                } else if (!e.additionally) {
                                    header.ViewModel.Solo = false;
                                }
                            } else {
                                if (track.TrackNo == e.trackNo || e.trackNo == -1) {
                                    header.ViewModel.Solo = false;
                                }
                            }
                        }
                    }
                    foreach (var (track, header) in trackHeaders) {
                        if (header.ViewModel != null) {
                            header.ViewModel.JudgeMuted();
                            header.ViewModel.ManuallyRaise();
                        }
                    }
                });
            MessageBus.Current.Listen<TracksMuteEvent>()
                .Subscribe(e => {
                    foreach (var (track, header) in trackHeaders) {
                        if (header.ViewModel != null) {
                            if(e.trackNo == -1) {
                                header.ViewModel.ToggleMute(e.allmute);
                            } else if (track.TrackNo == e.trackNo) {
                                header.ViewModel.ToggleMute();
                            }
                        }
                    }
                });
        }

        protected override void OnInitialized() {
            base.OnInitialized();
            trackAdder = new TrackAdder();
            trackAdder.Bind(this);
            Children.Add(trackAdder);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ItemsProperty) {
                if (change.OldValue != null && change.OldValue is ObservableCollection<UTrack> oldCol) {
                    oldCol.CollectionChanged -= Items_CollectionChanged;
                }
                if (change.NewValue != null && change.NewValue is ObservableCollection<UTrack> newCol) {
                    newCol.CollectionChanged += Items_CollectionChanged;
                }
            } else if (change.Property == DataContextProperty) {
                if (trackAdder != null) {
                    trackAdder.DataContext = DataContext;
                }
            }
        }

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null) {
                        foreach (var item in e.OldItems) {
                            if (item is UTrack track) {
                                Remove(track);
                            }
                        }
                    }
                    if (e.NewItems != null) {
                        foreach (var item in e.NewItems) {
                            if (item is UTrack track) {
                                Add(track);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var (track, _) in trackHeaders) {
                        Remove(track);
                    }
                    break;
            }
        }

        public string StartDrag(Point point) {
            lastTrackHeader = null;
            dragTrack = null;
            foreach (var (track, header) in trackHeaders) {
                if(header.Bounds.Contains(point)) {
                    curTrackHeader = header;
                    dragTrack = track;
                    break;
                }
            }

            if (dragTrack != null) {
                return dragTrack.TrackName;
            }
            return "";
        }
        public void UpdateDrag(Point point) {
            bool inHeader = false;
            foreach (var (track, header) in trackHeaders) {
                if (header.Bounds.Contains(point)) {
                    if(header != curTrackHeader) {
                        lastTrackHeader = curTrackHeader;
                        curTrackHeader = header;
                        lastTrackHeader?.hideDragLine();

                        if (point.Y > (curTrackHeader.Bounds.Top + (curTrackHeader.Bounds.Height / 2))) {
                            curTrackHeader.showDragLine(false);
                        } else {
                            curTrackHeader.showDragLine(true);
                        }
                    } else {
                        if (curTrackHeader != null) {
                            if (point.Y > (curTrackHeader.Bounds.Top + (curTrackHeader.Bounds.Height / 2))) {
                                curTrackHeader.showDragLine(false);
                            } else {
                                curTrackHeader.showDragLine(true);
                            }
                        }
                    }
                    inHeader = true;
                    break;
                }
            }
            if(!inHeader) {
                curTrackHeader?.hideDragLine();
                curTrackHeader = null;
            }
            //if (curTrackHeader != null && dragTrack != null) {
            //    if (trackHeaders[dragTrack] == curTrackHeader) {
            //        Cursor = ViewConstants.cursorNo;
            //    }
            //    else {
            //        Cursor = ViewConstants.cursorDragMove;
            //    }
            //}
        }
        public void StopDrag(Point point) {
            curTrackHeader?.hideDragLine();
            if(dragTrack == null) { return; }
            foreach (var (track, header) in trackHeaders) {
                if (header.Bounds.Contains(point)) {
                    int index = -1;
                    if (point.Y > (header.Bounds.Top + (header.Bounds.Height / 2))) {
                        index = track.TrackNo + 1;
                    } else {
                        index = track.TrackNo;
                    }

                    if(index == dragTrack.TrackNo || index == dragTrack.TrackNo + 1) {
                        return;
                    }

                    DocManager.Inst.StartUndoGroup();
                    DocManager.Inst.ExecuteCmd(new AdjustTrackCommand(DocManager.Inst.Project, dragTrack, index));
                    DocManager.Inst.EndUndoGroup();
                    break;
                }
            }
            curTrackHeader = null;
            lastTrackHeader = null;
            dragTrack = null;
        }

        void Add(UTrack track) {
            var vm = new TrackHeaderViewModel(track);
            var header = new TrackHeader() {
                DataContext = vm,
                ViewModel = vm,
            };
            header.Bind(track, this);
            Children.Add(header);
            trackHeaders.Add(track, header);
            if (trackAdder != null) {
                trackAdder.TrackNo = trackHeaders.Count;
            }
        }

        void Remove(UTrack track) {
            var header = trackHeaders[track];
            header.Dispose();
            trackHeaders.Remove(track);
            Children.Remove(header);
            if (trackAdder != null) {
                trackAdder.TrackNo = trackHeaders.Count;
            }
        }
    }
}
