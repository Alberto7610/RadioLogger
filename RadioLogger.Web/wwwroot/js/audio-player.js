window.audioPlayer = {
    play: async function () {
        var a = document.getElementById('streamAudio');
        if (!a) return 'Elemento de audio no encontrado';

        try {
            a.load();
            await a.play();
            return null; // success
        } catch (e) {
            if (e.name === 'NotAllowedError')
                return 'El navegador bloqueó la reproducción. Haz clic de nuevo.';
            if (e.name === 'NotSupportedError')
                return 'Formato de audio no soportado por el navegador.';
            return 'Error al reproducir: ' + e.message;
        }
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
