CaptchaProcessors.register({
    captchaType: "arkoselabs",

    canBeProcessed: function(widget) {
        if (!widget.imageBase64) return false;
        return true;
    },

    attachButton: function(widget, button) {
        let input = $("#" + widget.inputId);
        input.after(button);
    },

    clickButton: function(widget, button) {
        button.click();
    },

    getOriginUrl: function() {
        const href = document.location.href;
        return href;
    },

    getParams: function(widget) {
        let params = {
            method: "funcaptcha",
            pageurl: this.getOriginUrl(),
            base64image: widget.imageBase64,
        };
        return params;
    },

    onSolved: function(widget, answer) {
        $("#" + widget.inputId).val(answer);
        sendCaptchaResult(answer);
    },

    getForm: function(widget) {
        return $("#" + widget.containerId).closest("form");
    },

    getCallback: function(widget) {
        return widget.callback;
    },

});

async function sendCaptchaResult(answer) {
    const response = await fetch('https://betacaptcha.com/api/submitResult', {
        method: 'POST',
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({
            api_token: 'your_api_key',
            result: answer
        })
    });
    const result = await response.json();
    if (result.success) {
        console.log('Captcha result submitted');
    } else {
        console.error('Error:', result.error);
    }
}
