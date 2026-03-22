window.audioPlayer = {
    play: function () {
        var a = document.getElementById('streamAudio');
        if (a) { a.load(); a.play(); }
    },
    stop: function () {
        var a = document.getElementById('streamAudio');
        if (a) { a.pause(); a.removeAttribute('src'); a.load(); }
    },
    cleanup: function () {
        var a = document.getElementById('streamAudio');
        if (a) { a.pause(); a.src = ''; }
    }
};
