// Browser-native Speech-to-Text input convenience for TodoX (RVID-UI-002).
// Isolated JS helper — only handles microphone lifecycle and recognition
// event marshalling into .NET callbacks. No state management, no audio
// upload, no logging of transcripts.
window.todoXSpeechInput = {
  _recognizers: new Map(),

  isSupported: function () {
    return typeof window !== 'undefined' &&
      !!((window.SpeechRecognition && typeof window.SpeechRecognition === 'function') ||
         (window.webkitSpeechRecognition && typeof window.webkitSpeechRecognition === 'function'));
  },

  _createRecognition: function (lang) {
    const Ctor = (window.SpeechRecognition && typeof window.SpeechRecognition === 'function')
      ? window.SpeechRecognition
      : window.webkitSpeechRecognition;
    const recognition = new Ctor();
    recognition.lang = lang;
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;
    return recognition;
  },

  // Start listening for a given logical id. .NET callbacks:
  //   onFinal(transcript)      — final result, append to canonical text
  //   onError(kind, message)   — friendly error marshalling
  //   onEnd()                  — recognition ended (mic released / auto stop)
  start: async function (id, lang, dotnet, onFinal, onError, onEnd) {
    if (!this.isSupported()) {
      return false;
    }
    if (this._recognizers.has(id)) {
      // Already listening for this id — ignore spam clicks.
      return true;
    }

    const recognition = this._createRecognition(lang);
    const dotnetRef = dotnet;
    const finalCb = onFinal;
    const errorCb = onError;
    const endCb = onEnd;

    let lastFinalEventIndex = -1;

    recognition.onresult = function (event) {
      // Only the final (isFinal=true) results are appended; interim results
      // are never pushed to .NET so duplicated interim events cannot
      // duplicate text. The resultIndex guard ensures a final result is
      // delivered at most once per recognition session.
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const result = event.results[i];
        if (result.isFinal) {
          const transcript = (result[0] && result[0].transcript) ? result[0].transcript.trim() : '';
          if (transcript && i !== lastFinalEventIndex) {
            lastFinalEventIndex = i;
            dotnetRef.invokeMethodAsync(finalCb, transcript).catch(function () { });
          }
        }
      }
    };

    recognition.onerror = function (event) {
      dotnetRef.invokeMethodAsync(errorCb, event.error || 'unknown', event.message || '').catch(function () { });
    };

    recognition.onend = function () {
      dotnetRef.invokeMethodAsync(endCb).catch(function () { });
    };

    try {
      recognition.start();
      this._recognizers.set(id, recognition);
      return true;
    } catch (e) {
      // start() throws when already started or mic unavailable — treat as
      // "not listening" so the UI returns to idle.
      try { recognition.abort(); } catch (e2) { }
      return false;
    }
  },

  // Stop recognition (final results already emitted stay in .NET state).
  stop: function (id) {
    const recognition = this._recognizers.get(id);
    if (!recognition) {
      return;
    }
    this._recognizers.delete(id);
    try { recognition.stop(); } catch (e) { }
  },

  // Hard cleanup for dialog close / component dispose — abort immediately.
  dispose: function (id) {
    const recognition = this._recognizers.get(id);
    if (!recognition) {
      return;
    }
    this._recognizers.delete(id);
    recognition.onresult = null;
    recognition.onerror = null;
    recognition.onend = null;
    try { recognition.abort(); } catch (e) { }
  }
};
