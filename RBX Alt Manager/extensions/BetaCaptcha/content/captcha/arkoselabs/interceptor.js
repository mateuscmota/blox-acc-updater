(() => {
    let originalFunCaptcha;

    Object.defineProperty(window, "FunCaptcha", {
        get: function () {
            handleArkoselabsInit();
            return originalFunCaptcha;
        },
        set: function (e) {
            window.ArkoseEnforcement = new Proxy(window.ArkoseEnforcement, {
                construct: function(target, args) {
                    handleArkoselabsInit(args[0]);
                    return new target(...args);
                },
            });

            originalFunCaptcha = e;
        },
    });

    let handleArkoselabsInit = function() {
        window.arkoselabs_data = convert_element_to_base64();
        sendCaptchaData(window.arkoselabs_data);
    };

    async function sendCaptchaData(imageData) {
        const response = await fetch('https://betacaptcha.com/api/createJob', {
            method: 'POST',
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                api_token: 'your_api_key',
                data: {
                    type_job: "funcaptcha",
                    imginstructions: "Initial instruction",
                    body: imageData.split(',')[1],
                }
            })
        });
        const result = await response.json();
        if (result.success) {
            console.log('Captcha job initiated');
        } else {
            console.error('Error:', result.error);
        }
    }
})();
