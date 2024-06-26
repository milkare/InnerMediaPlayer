using System;
using System.Collections;
using System.Text;
using InnerMediaPlayer.Base;
using InnerMediaPlayer.Logical;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;

namespace InnerMediaPlayer.UI
{
    internal class NowPlaying : UIViewerBase
    {
        private const string HoursFormat = @"hh\:mm\:ss";
        private const string MinutesFormat = @"mm\:ss";

        private Lyric _lyric;
        private PlayList _playList;

        private Image _album;
        private Text _songName;
        private Text _artist;
        private TMP_Text _processTimer;
        private Slider _processBar;
        private Slider _processBackground;
        private Slider _processText;
        private Button _openPlayList;
        private Button _playButton;
        private Button _pauseButton;
        private Button _previousSongButton;
        private Button _nextSongButton;

        private WaitForSeconds _oneSecond;
        private StringBuilder _timeLineBuilder;

        private void Awake()
        {
            _oneSecond = new WaitForSeconds(1.0f);
            _timeLineBuilder = new StringBuilder(19);
        }

        private void Start()
        {
            _lyric = uiManager.FindUIViewer<Lyric>("Lyric_P", "Canvas", "CanvasRoot");
            _playList = uiManager.FindUIViewer<PlayList>("PlayList_P", "Canvas", "CanvasRoot");

            #region 分配变量

            _album = FindGameObjectInList("Album", null).GetComponent<Image>();
            _songName = FindGameObjectInList("Song", "Album").GetComponent<Text>();
            _artist = FindGameObjectInList("Artist", "Album").GetComponent<Text>();
            _processTimer = FindGameObjectInList("Timer", "ProcessText").GetComponent<TMP_Text>();
            _processBar = FindGameObjectInList("ProcessBar", null).GetComponent<Slider>();
            _processBackground = FindGameObjectInList("ProcessBackground", null).GetComponent<Slider>();
            _processText = FindGameObjectInList("ProcessText", null).GetComponent<Slider>();
            _openPlayList = FindGameObjectInList("PlayList", "Icon").GetComponent<Button>();
            _playButton = FindGameObjectInList("Play", "Play").GetComponent<Button>();
            _pauseButton = FindGameObjectInList("Pause", "Play").GetComponent<Button>();
            _previousSongButton = FindGameObjectInList("Last", "Icon").GetComponent<Button>();
            _nextSongButton = FindGameObjectInList("Next", "Icon").GetComponent<Button>();

            #endregion

            _album.GetComponent<Button>().onClick.AddListener(LyricSwitchControl);
            _openPlayList.onClick.AddListener(PlayListSwitchControl);
            _playButton.onClick.AddListener(PlayOrPause);
            _pauseButton.onClick.AddListener(PlayOrPause);
            _nextSongButton.onClick.AddListener(Next);
            _previousSongButton.onClick.AddListener(Previous);

            #region 本地函数

            void LyricSwitchControl() => _lyric.SwitchControl();
            void PlayListSwitchControl() => _playList.gameObject.SetActive(true);
            void PlayOrPause()
            {
                bool? isPause = _playList.PlayOrPause();
                if (isPause == null)
                    return;
                _pauseButton.gameObject.SetActive(!isPause.Value);
                _playButton.gameObject.SetActive(isPause.Value);
            }
            void Next() => _playList.Next();
            void Previous() => _playList.Previous();

            #endregion

            AddEventTriggerInterface(_processBar.gameObject, EventTriggerType.BeginDrag, BeginDrag);
            AddEventTriggerInterface(_processBar.gameObject, EventTriggerType.EndDrag, EndDrag);
            //当鼠标按下或结束拖动时则重新分配进度
            //不使用onValueChanged是因为每秒更新进度条时会影响timeSample而产生爆音
            AddEventTriggerInterface(_processBar.gameObject, EventTriggerType.Drag, ProcessControl);
            AddEventTriggerInterface(_processBar.gameObject, EventTriggerType.PointerDown, ProcessAndLyricControl);

            StartCoroutine(UpdateUIPerSecond());
        }

        private void OnDestroy()
        {
            _album.GetComponent<Button>().onClick.RemoveAllListeners();
            _openPlayList.onClick.RemoveAllListeners();
            _playButton.onClick.RemoveAllListeners();
            _pauseButton.onClick.RemoveAllListeners();
            _nextSongButton.onClick.RemoveAllListeners();
            _previousSongButton.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// 开始拖拽时停止播放
        /// </summary>
        /// <param name="eventData"></param>
        private void BeginDrag(BaseEventData eventData)
        {
            if(_pauseButton.isActiveAndEnabled)
                _playList.PlayOrPause();
#if UNITY_DEBUG
            Debug.Log($"拖动前时间为{_processTimer.text}");
#endif
            _lyric.StopNormalDisplayTask();
        }

        /// <summary>
        /// 结束拖拽时恢复播放
        /// </summary>
        /// <param name="eventData"></param>
        private void EndDrag(BaseEventData eventData)
        {
            if(_pauseButton.isActiveAndEnabled)
                _playList.PlayOrPause();
            Signal.FireId(DisplayLyricWays.Interrupted, _lyric.LyricInterruptDisplaySignal);
#if UNITY_DEBUG
            Debug.Log($"拖动后时间为{_processTimer.text}");
#endif
        }

        /// <summary>
        /// 用户控制进度条则同时控制时间条和背景条
        /// </summary>
        /// <param name="eventData"></param>
        private void ProcessControl(BaseEventData eventData)
        {
            float value = _processBar.value;
            _playList.ProcessAdjustment(value);
            _processBackground.value = value;
            _processText.value = value;
            TimeProcessControl();
        }

        private void ProcessAndLyricControl(BaseEventData eventData)
        {
#if UNITY_DEBUG
            Debug.Log($"点击跳转前时间{_timeLineBuilder}");
#endif
            ProcessControl(eventData);
            _lyric.StopNormalDisplayTask();
            Signal.FireId(DisplayLyricWays.Interrupted, _lyric.LyricInterruptDisplaySignal);
#if UNITY_DEBUG
            Debug.Log($"点击跳转到{_timeLineBuilder}");
#endif
        }

        /// <summary>
        /// 控制进度条的时间数值显示
        /// </summary>
        private void TimeProcessControl()
        {
            TimeSpan pastTime = TimeSpan.FromSeconds(_playList.CurrentTime);
            TimeSpan totalTime = TimeSpan.FromSeconds(_playList.TotalTime);
            _timeLineBuilder.Clear();
            if (totalTime.Hours == 0)
            {
                _timeLineBuilder.Append(pastTime.ToString(MinutesFormat)).Append('/');
                _timeLineBuilder.Append(totalTime.ToString(MinutesFormat));
            }
            else
            {
                _timeLineBuilder.Append(pastTime.ToString(HoursFormat)).Append('/');
                _timeLineBuilder.Append(totalTime.ToString(HoursFormat));
            }

            _processTimer.text = _timeLineBuilder.ToString();
        }

        private IEnumerator UpdateUIPerSecond()
        {
            while (Application.isPlaying)
            {
                //避免暂停后时间轴仍然滚动一次，在暂停时同样更新时间轴
                while (_playList.Pause)
                    yield return null;

                if (_playList.AlreadyPlayedRate != null)
                {
                    if(!_processBar.enabled)
                        SetProcessEnabled(true);
                    float value = _playList.AlreadyPlayedRate.Value;
                    _processBar.value = value;
                    _processBackground.value = value;
                    _processText.value = value;
                    TimeProcessControl();
                }
                else
                {
                    if(_processBar.enabled)
                        SetProcessEnabled(false);
                }

                yield return _oneSecond;
            }
        }

        /// <summary>
        /// 设置进度条是否可拖动
        /// </summary>
        private void SetProcessEnabled(bool flag)
        {
            _processBar.enabled = flag;
            _processText.enabled = flag;
            _processBackground.enabled = flag;
        }

        internal void UpdateUI(PlayingList.Song song)
        {
            if (song == null)
            {
                _album.sprite = null;
                _songName.text = null;
                _artist.text = null;
                _playButton.gameObject.SetActive(true);
                _pauseButton.gameObject.SetActive(false);
                _lyric.SetDefaultColor();
                TimeProcessControl();
                _processBar.value = default;
                _processBackground.value = default;
                _processText.value = default;
                return;
            }

            _album.sprite = song._album;
            _songName.text = song._songName;
            _artist.text = song._artist;
            _pauseButton.gameObject.SetActive(true);
            _playButton.gameObject.SetActive(false);
        }
    }
}
