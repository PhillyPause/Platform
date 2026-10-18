/* The explainer timeline.

   This module owns the clock and nothing else. It toggles .is-on for the
   active scene and writes the caption; every pixel of motion is CSS. That is
   deliberate — see Docs/EXPLAINER.md and the "no per-frame C#" rule in
   CLAUDE.md. The tick runs five times a second, not sixty, because scene
   boundaries are seconds apart.

   Narration is optional. If media/explainer.mp3 is missing or the browser
   refuses to play it, the piece runs on a timer with the same captions. */

const SCENES = [
  { at: 0, text: 'In 2023, hundreds of the people building artificial intelligence signed a single sentence.' },
  { at: 9, text: 'It said that the risk of human extinction from AI should be a global priority — alongside pandemics and nuclear war.' },
  { at: 21, text: 'Then they went back to work. Investment climbed. Capability climbed. The part nobody had solved stayed unsolved.' },
  { at: 31, text: 'Nobody can explain how these systems work. They are not built. They are grown — adjusted, billions of times, until the output looks right.' },
  { at: 44, text: 'So why not slow down? Because nobody can slow down alone. Safety costs time. Time costs the lead. Whoever hesitates, loses.' },
  { at: 56, text: 'That is a problem rules fix, not engineers. And there is a lever: these systems need enormous numbers of specialised chips, from a handful of suppliers.' },
  { at: 68, text: 'Countable. Traceable. Already controlled. Pause the largest training runs until safety can be shown.' },
  { at: 79, text: 'That decision gets made in Washington. Pennsylvania has nineteen votes there. You could be the first to call.' }
];

const DURATION = 92;
const TICK_MS = 200;

function setUp(root) {
  const stage = root.querySelector('[data-stage]');
  const scenes = [...root.querySelectorAll('.ex-scene')];
  const caption = root.querySelector('[data-caption]');
  const controls = root.querySelector('[data-controls]');
  const playButton = root.querySelector('[data-play]');
  const soundButton = root.querySelector('[data-sound]');
  const poster = root.querySelector('[data-poster]');
  const dots = [...root.querySelectorAll('[data-seek]')];
  const audio = root.querySelector('[data-audio]');

  if (!stage || scenes.length !== SCENES.length) return;

  // Hidden in the markup so a reader without JavaScript is not shown controls
  // that cannot work. They read the transcript instead.
  controls?.removeAttribute('hidden');

  let index = -1;
  let elapsed = 0;
  let startedAt = 0;
  let timer = null;
  let useAudio = false;

  const indexFor = (seconds) => {
    let found = 0;
    for (let i = 0; i < SCENES.length; i += 1) {
      if (seconds >= SCENES[i].at) found = i;
    }
    return found;
  };

  function show(next) {
    if (next === index) return;
    index = next;

    scenes.forEach((scene, i) => scene.classList.toggle('is-on', i === index));
    caption.textContent = SCENES[index].text;

    dots.forEach((dot, i) => {
      dot.setAttribute('aria-current', String(i === index));
      dot.classList.toggle('is-seen', i < index);
    });
  }

  function now() {
    if (useAudio && !audio.paused) return audio.currentTime;
    if (useAudio) return audio.currentTime;
    return elapsed + (startedAt ? (performance.now() - startedAt) / 1000 : 0);
  }

  function tick() {
    const seconds = now();

    if (seconds >= DURATION) {
      stop();
      // Leave the last scene up rather than snapping back to the poster: the
      // call to action is the point of the whole piece.
      show(SCENES.length - 1);
      return;
    }

    show(indexFor(seconds));
  }

  function start() {
    root.classList.add('is-playing', 'is-started');
    playButton?.setAttribute('aria-label', 'Pause');

    if (useAudio) {
      audio.play().catch(() => {
        // Autoplay policy, a missing file, or an unsupported codec. Run on the
        // clock instead — the captions carry the script either way.
        useAudio = false;
        startedAt = performance.now();
      });
    } else {
      startedAt = performance.now();
    }

    clearInterval(timer);
    timer = setInterval(tick, TICK_MS);
    tick();
  }

  function stop() {
    root.classList.remove('is-playing');
    playButton?.setAttribute('aria-label', 'Play');

    if (useAudio) {
      audio.pause();
    } else if (startedAt) {
      elapsed += (performance.now() - startedAt) / 1000;
      startedAt = 0;
    }

    clearInterval(timer);
    timer = null;
  }

  function seek(seconds) {
    if (useAudio) {
      audio.currentTime = seconds;
    } else {
      elapsed = seconds;
      if (startedAt) startedAt = performance.now();
    }
    tick();
  }

  // Probe for narration without blocking: preload is "none", so this only
  // tells us whether the browser thinks it can play the type at all. A missing
  // file surfaces as an error event and flips us back to the timer.
  if (audio?.getAttribute('src')) {
    useAudio = audio.canPlayType('audio/mpeg') !== '';
    audio.addEventListener('error', () => {
      useAudio = false;
      if (root.classList.contains('is-playing')) startedAt = performance.now();
    });
    audio.addEventListener('ended', () => {
      stop();
      show(SCENES.length - 1);
    });
  }

  playButton?.addEventListener('click', () => {
    root.classList.contains('is-playing') ? stop() : start();
  });

  poster?.addEventListener('click', start);

  soundButton?.addEventListener('click', () => {
    if (!audio) return;
    audio.muted = !audio.muted;
    root.classList.toggle('is-muted', audio.muted);
    soundButton.setAttribute('aria-label', audio.muted ? 'Sound on' : 'Sound off');
  });

  dots.forEach((dot) => {
    dot.addEventListener('click', () => {
      const target = SCENES[Number(dot.dataset.seek) - 1];
      if (!target) return;
      seek(target.at);
      if (!root.classList.contains('is-playing')) {
        root.classList.add('is-started');
      }
    });
  });

  // Stop when scrolled well away: narration continuing out of sight is the
  // single most irritating thing an autoplaying explainer can do.
  if ('IntersectionObserver' in window) {
    new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting && root.classList.contains('is-playing')) stop();
      },
      { threshold: 0.25 }
    ).observe(stage);
  }

  show(0);
}

document.querySelectorAll('[data-explainer]').forEach(setUp);
