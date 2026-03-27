// WebGL2 Rendering Interop for Intersect Engine
window.IntersectWebGL = (() => {
    let gl = null;
    let canvas = null;
    let displayCanvas = null;
    let displayCtx = null;
    let textCanvas = null;
    let textCtx = null;

    // Shader programs
    let spriteProgram = null;
    let currentProgram = null;

    // Sprite batching
    const MAX_SPRITES = 4096;
    let spriteVAO = null;
    let spriteVBO = null;
    let spriteEBO = null;
    let batchVertices = null;
    let batchCount = 0;
    let batchTextureId = -1;

    // Textures
    const textures = new Map();
    let nextTextureId = 1;

    // Framebuffers (render targets)
    const framebuffers = new Map();
    let nextFbId = 1;
    let activeFramebuffer = null;

    // Custom shader programs
    const shaderPrograms = new Map();
    let nextShaderId = 1;

    // GPU Buffers
    const gpuBuffers = new Map();
    let nextBufferId = 1;

    // Cached pixel data for GetPixel (skin color reading)
    const texturePixelData = new Map();

    // State
    let viewMatrix = new Float32Array(16);
    let screenWidth = 800;
    let screenHeight = 600;
    let currentBlendMode = 0; // 0=Alpha, 1=Multiply, 2=Add, 3=Opaque, 4=Cutout

    // White pixel texture for solid color fills
    let whitePixelTexId = -1;

    function identity4x4(out) {
        out[0]=1; out[1]=0; out[2]=0; out[3]=0;
        out[4]=0; out[5]=1; out[6]=0; out[7]=0;
        out[8]=0; out[9]=0; out[10]=1; out[11]=0;
        out[12]=0; out[13]=0; out[14]=0; out[15]=1;
    }

    function ortho4x4(out, left, right, bottom, top, near, far) {
        const lr = 1 / (left - right);
        const bt = 1 / (bottom - top);
        const nf = 1 / (near - far);
        out[0] = -2 * lr; out[1] = 0; out[2] = 0; out[3] = 0;
        out[4] = 0; out[5] = -2 * bt; out[6] = 0; out[7] = 0;
        out[8] = 0; out[9] = 0; out[10] = 2 * nf; out[11] = 0;
        out[12] = (left + right) * lr;
        out[13] = (top + bottom) * bt;
        out[14] = (far + near) * nf;
        out[15] = 1;
    }

    const VERT_SRC = `#version 300 es
        layout(location = 0) in vec2 a_position;
        layout(location = 1) in vec2 a_texCoord;
        layout(location = 2) in vec4 a_color;
        uniform mat4 u_projection;
        out vec2 v_texCoord;
        out vec4 v_color;
        void main() {
            gl_Position = u_projection * vec4(a_position, 0.0, 1.0);
            v_texCoord = a_texCoord;
            v_color = a_color;
        }
    `;

    const FRAG_SRC = `#version 300 es
        precision mediump float;
        in vec2 v_texCoord;
        in vec4 v_color;
        uniform sampler2D u_texture;
        out vec4 fragColor;
        void main() {
            vec4 texColor = texture(u_texture, v_texCoord);
            fragColor = texColor * v_color;
        }
    `;

    const FRAG_MULTIPLY_SRC = `#version 300 es
        precision mediump float;
        in vec2 v_texCoord;
        in vec4 v_color;
        uniform sampler2D u_texture;
        out vec4 fragColor;
        void main() {
            vec4 texColor = texture(u_texture, v_texCoord);
            fragColor = texColor * v_color;
            fragColor.rgb *= fragColor.a;
        }
    `;

    function compileShader(type, src) {
        const s = gl.createShader(type);
        gl.shaderSource(s, src);
        gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {
            console.error('Shader compile error:', gl.getShaderInfoLog(s));
            gl.deleteShader(s);
            return null;
        }
        return s;
    }

    function createProgramFromSources(vSrc, fSrc) {
        const vs = compileShader(gl.VERTEX_SHADER, vSrc);
        const fs = compileShader(gl.FRAGMENT_SHADER, fSrc);
        if (!vs || !fs) return null;
        const prog = gl.createProgram();
        gl.attachShader(prog, vs);
        gl.attachShader(prog, fs);
        gl.linkProgram(prog);
        if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
            console.error('Program link error:', gl.getProgramInfoLog(prog));
            gl.deleteProgram(prog);
            return null;
        }
        gl.deleteShader(vs);
        gl.deleteShader(fs);
        return prog;
    }

    function initBatchBuffers() {
        // 4 vertices per sprite, 8 floats per vertex (x, y, u, v, r, g, b, a)
        batchVertices = new Float32Array(MAX_SPRITES * 4 * 8);

        spriteVAO = gl.createVertexArray();
        gl.bindVertexArray(spriteVAO);

        spriteVBO = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, spriteVBO);
        gl.bufferData(gl.ARRAY_BUFFER, batchVertices.byteLength, gl.DYNAMIC_DRAW);

        // Position (vec2)
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 32, 0);
        // TexCoord (vec2)
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 32, 8);
        // Color (vec4)
        gl.enableVertexAttribArray(2);
        gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 32, 16);

        // Index buffer: 6 indices per sprite (2 triangles)
        const indices = new Uint16Array(MAX_SPRITES * 6);
        for (let i = 0; i < MAX_SPRITES; i++) {
            const vi = i * 4;
            const ii = i * 6;
            indices[ii] = vi;
            indices[ii + 1] = vi + 1;
            indices[ii + 2] = vi + 2;
            indices[ii + 3] = vi;
            indices[ii + 4] = vi + 2;
            indices[ii + 5] = vi + 3;
        }
        spriteEBO = gl.createBuffer();
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, spriteEBO);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);

        gl.bindVertexArray(null);
    }

    function flushBatch() {
        if (batchCount === 0) return;

        gl.bindVertexArray(spriteVAO);
        gl.bindBuffer(gl.ARRAY_BUFFER, spriteVBO);
        gl.bufferSubData(gl.ARRAY_BUFFER, 0, batchVertices.subarray(0, batchCount * 4 * 8));

        if (batchTextureId > 0) {
            const tex = textures.get(batchTextureId);
            if (tex) {
                gl.activeTexture(gl.TEXTURE0);
                gl.bindTexture(gl.TEXTURE_2D, tex.glTexture);
            }
        }

        gl.drawElements(gl.TRIANGLES, batchCount * 6, gl.UNSIGNED_SHORT, 0);
        gl.bindVertexArray(null);
        batchCount = 0;
    }

    function setBlendMode(mode) {
        if (mode === currentBlendMode) return;
        flushBatch();
        currentBlendMode = mode;
        switch (mode) {
            case 0: // Alpha
                gl.enable(gl.BLEND);
                gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
                break;
            case 1: // Multiply
                gl.enable(gl.BLEND);
                gl.blendFunc(gl.DST_COLOR, gl.ONE_MINUS_SRC_ALPHA);
                break;
            case 2: // Add
                gl.enable(gl.BLEND);
                gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
                break;
            case 3: // Opaque
                gl.disable(gl.BLEND);
                break;
            case 4: // Cutout
                gl.enable(gl.BLEND);
                gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
                break;
        }
    }

    return {
        init(canvasId) {
            canvas = document.getElementById(canvasId);
            if (!canvas) { console.error('Canvas not found:', canvasId); return false; }

            gl = canvas.getContext('webgl2', {
                alpha: true,
                antialias: false,
                premultipliedAlpha: true,
                preserveDrawingBuffer: true,
                powerPreference: 'high-performance'
            });
            if (!gl) { console.error('WebGL2 not supported'); return false; }

            displayCanvas = document.getElementById('display-canvas');
            if (displayCanvas) {
                displayCtx = displayCanvas.getContext('2d');
                displayCanvas.width = canvas.width || 800;
                displayCanvas.height = canvas.height || 600;

                // TEST: Draw directly on 2D canvas to verify it's visible
                displayCtx.fillStyle = 'red';
                displayCtx.fillRect(0, 0, displayCanvas.width, displayCanvas.height);
                displayCtx.fillStyle = 'white';
                displayCtx.font = '48px sans-serif';
                displayCtx.fillText('DISPLAY CANVAS TEST', 20, 80);
                console.log('[DISPLAY_TEST] Drew red rect + text on display-canvas. If you see it, 2D canvas works.');
            } else {
                console.error('[DISPLAY_TEST] display-canvas element NOT FOUND');
            }

            textCanvas = document.getElementById('text-canvas');
            if (textCanvas) textCtx = textCanvas.getContext('2d');

            spriteProgram = createProgramFromSources(VERT_SRC, FRAG_SRC);
            if (!spriteProgram) return false;
            currentProgram = spriteProgram;
            gl.useProgram(spriteProgram);

            initBatchBuffers();

            // Create 1x1 white pixel
            const whiteTex = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, whiteTex);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE,
                new Uint8Array([255, 255, 255, 255]));
            whitePixelTexId = nextTextureId++;
            textures.set(whitePixelTexId, { glTexture: whiteTex, width: 1, height: 1 });

            gl.enable(gl.BLEND);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
            gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);

            // === SANITY TEST: Draw a bright magenta rectangle to verify WebGL output ===
            {
                const err0 = gl.getError();
                if (err0 !== gl.NO_ERROR) console.error('[WEBGL_SANITY] GL error before test:', err0);

                gl.viewport(0, 0, canvas.width, canvas.height);
                gl.clearColor(1, 0, 1, 1); // magenta
                gl.clear(gl.COLOR_BUFFER_BIT);

                const err1 = gl.getError();
                if (err1 !== gl.NO_ERROR) console.error('[WEBGL_SANITY] GL error after clear:', err1);

                // Read back a pixel to verify
                const pixel = new Uint8Array(4);
                gl.readPixels(10, 10, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
                const err2 = gl.getError();
                console.log(`[WEBGL_SANITY] Test pixel readback: rgba(${pixel[0]},${pixel[1]},${pixel[2]},${pixel[3]}) err=${err2}`);
                console.log(`[WEBGL_SANITY] Canvas: ${canvas.width}x${canvas.height}, clientSize: ${canvas.clientWidth}x${canvas.clientHeight}`);
                console.log(`[WEBGL_SANITY] Canvas visible: display=${getComputedStyle(canvas).display}, visibility=${getComputedStyle(canvas).visibility}, opacity=${getComputedStyle(canvas).opacity}`);
                console.log(`[WEBGL_SANITY] Canvas zIndex: ${getComputedStyle(canvas).zIndex}, position: ${getComputedStyle(canvas).position}`);
                console.log(`[WEBGL_SANITY] Canvas bounding rect:`, JSON.stringify(canvas.getBoundingClientRect()));

                // Check what's on top of the canvas center
                const rect = canvas.getBoundingClientRect();
                const topEl = document.elementFromPoint(rect.left + rect.width/2, rect.top + rect.height/2);
                console.log(`[WEBGL_SANITY] Element at canvas center:`, topEl?.tagName, topEl?.id, topEl?.className);

                // Leave the magenta for 1 frame so user can see it flash
                console.log('[WEBGL_SANITY] If you see a magenta flash, WebGL output is working. If not, canvas is obscured or GL context is broken.');
            }

            // Handle WebGL context loss (M3 fix)
            canvas.addEventListener('webglcontextlost', (e) => {
                e.preventDefault();
                console.warn('WebGL context lost');
            });
            canvas.addEventListener('webglcontextrestored', () => {
                console.log('WebGL context restored, reinitializing...');
                // Reinitialize shaders and buffers
                spriteProgram = createProgramFromSources(VERT_SRC, FRAG_SRC);
                if (spriteProgram) {
                    currentProgram = spriteProgram;
                    gl.useProgram(spriteProgram);
                }
                initBatchBuffers();
                // Textures will need to be reloaded by the C# side
            });

            return true;
        },

        getWhitePixelTextureId() { return whitePixelTexId; },

        resize(width, height) {
            screenWidth = width;
            screenHeight = height;
            canvas.width = width;
            canvas.height = height;
            gl.viewport(0, 0, width, height);

            if (displayCanvas) {
                displayCanvas.width = width;
                displayCanvas.height = height;
            }

            if (textCanvas) {
                textCanvas.width = width;
                textCanvas.height = height;
            }
        },

        beginFrame() {
            batchCount = 0;
            batchTextureId = -1;
            gl.useProgram(spriteProgram);
            currentProgram = spriteProgram;
            ortho4x4(viewMatrix, 0, screenWidth, screenHeight, 0, -1, 1);
            const loc = gl.getUniformLocation(spriteProgram, 'u_projection');
            gl.uniformMatrix4fv(loc, false, viewMatrix);
            setBlendMode(0);
        },

        endFrame() {
            flushBatch();

            // Copy WebGL output to 2D display canvas
            if (displayCtx) {
                displayCtx.drawImage(canvas, 0, 0);
                if (this._endFrameCount === undefined) this._endFrameCount = 0;
                this._endFrameCount++;
                if (this._endFrameCount <= 5) {
                    // Verify the copy worked by reading a pixel from the 2D canvas
                    try {
                        const px = displayCtx.getImageData(Math.floor(screenWidth/2), Math.floor(screenHeight/2), 1, 1).data;
                        console.log(`[DISPLAY] endFrame #${this._endFrameCount}: display center pixel = rgba(${px[0]},${px[1]},${px[2]},${px[3]}), canvas=${canvas.width}x${canvas.height}, display=${displayCanvas.width}x${displayCanvas.height}`);
                    } catch(e) {
                        console.error(`[DISPLAY] getImageData failed: ${e.message}`);
                    }
                }
            } else {
                if (!this._noCtxWarned) {
                    console.error('[DISPLAY] displayCtx is null - 2D canvas copy disabled');
                    this._noCtxWarned = true;
                }
            }
        },

        clear(r, g, b, a) {
            gl.clearColor(r, g, b, a);
            gl.clear(gl.COLOR_BUFFER_BIT);
        },

        setView(x, y, width, height) {
            flushBatch();
            ortho4x4(viewMatrix, x, x + width, y + height, y, -1, 1);
            const loc = gl.getUniformLocation(currentProgram, 'u_projection');
            gl.uniformMatrix4fv(loc, false, viewMatrix);
        },

        setViewport(x, y, w, h) {
            flushBatch();
            gl.viewport(x, y, w, h);
        },

        restoreViewport() {
            flushBatch();
            gl.viewport(0, 0, canvas.width, canvas.height);
        },

        setScissor(x, y, w, h) {
            flushBatch();
            gl.enable(gl.SCISSOR_TEST);
            gl.scissor(x, canvas.height - y - h, w, h);
        },

        clearScissor() {
            flushBatch();
            gl.disable(gl.SCISSOR_TEST);
        },

        setBlendMode(mode) { setBlendMode(mode); },

        // Texture management
        createTextureFromImage(imageData, width, height) {
            const tex = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, imageData);
            const id = nextTextureId++;
            textures.set(id, { glTexture: tex, width, height });
            return id;
        },

        createTextureFromUrl(url) {
            const self = this;
            return new Promise((resolve, reject) => {
                function loadImage(imageUrl, isRetry) {
                    const img = new Image();
                    img.crossOrigin = 'anonymous';
                    img.onload = () => {
                        const tex = gl.createTexture();
                        gl.bindTexture(gl.TEXTURE_2D, tex);
                        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
                        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
                        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
                        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
                        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
                        const id = nextTextureId++;
                        textures.set(id, { glTexture: tex, width: img.width, height: img.height });

                        // Cache pixel data for GetPixel (used by skin color initialization)
                        try {
                            const tmpCanvas = document.createElement('canvas');
                            tmpCanvas.width = img.width;
                            tmpCanvas.height = img.height;
                            const tmpCtx = tmpCanvas.getContext('2d');
                            tmpCtx.drawImage(img, 0, 0);
                            texturePixelData.set(id, tmpCtx.getImageData(0, 0, img.width, img.height));
                        } catch (e) {
                            // CORS or other issue — pixel reading won't be available for this texture
                        }

                        resolve({ id, width: img.width, height: img.height });
                    };
                    img.onerror = () => {
                        // On Linux, filenames are case-sensitive. Try lowercase filename as fallback.
                        if (!isRetry) {
                            const lastSlash = imageUrl.lastIndexOf('/');
                            if (lastSlash >= 0) {
                                const dir = imageUrl.substring(0, lastSlash + 1);
                                const file = imageUrl.substring(lastSlash + 1).toLowerCase();
                                const lowerUrl = dir + file;
                                if (lowerUrl !== imageUrl) {
                                    loadImage(lowerUrl, true);
                                    return;
                                }
                            }
                        }
                        reject(new Error('Failed to load: ' + url));
                    };
                    img.src = imageUrl;
                }
                loadImage(url, false);
            });
        },

        // Synchronous texture load using sync XHR + base64 data URI
        // Used for critical textures (UI skin) that must be available immediately
        createTextureFromUrlSync(url) {
            try {
                // Fetch raw bytes via sync XHR using overrideMimeType to get binary
                let xhr = new XMLHttpRequest();
                xhr.open('GET', url, false); // synchronous
                xhr.overrideMimeType('text/plain; charset=x-user-defined');
                xhr.send();
                // Case-insensitive fallback: try lowercase filename on 404
                if (xhr.status === 404) {
                    const lastSlash = url.lastIndexOf('/');
                    if (lastSlash >= 0) {
                        const lowerUrl = url.substring(0, lastSlash + 1) + url.substring(lastSlash + 1).toLowerCase();
                        if (lowerUrl !== url) {
                            xhr = new XMLHttpRequest();
                            xhr.open('GET', lowerUrl, false);
                            xhr.overrideMimeType('text/plain; charset=x-user-defined');
                            xhr.send();
                        }
                    }
                }
                if (xhr.status !== 200) return null;

                // Convert binary string to base64 data URI
                const raw = xhr.responseText;
                let binary = '';
                for (let i = 0; i < raw.length; i++) {
                    binary += String.fromCharCode(raw.charCodeAt(i) & 0xff);
                }
                const b64 = btoa(binary);
                const ext = url.toLowerCase().endsWith('.jpg') || url.toLowerCase().endsWith('.jpeg') ? 'jpeg' : 'png';

                const img = new Image();
                img.src = `data:image/${ext};base64,${b64}`;

                // Data URIs decode synchronously
                if (img.naturalWidth === 0 || img.naturalHeight === 0) return null;

                const tex = gl.createTexture();
                gl.bindTexture(gl.TEXTURE_2D, tex);
                gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
                gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
                gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
                gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
                gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
                const id = nextTextureId++;
                textures.set(id, { glTexture: tex, width: img.naturalWidth, height: img.naturalHeight });

                // Cache pixel data for GetPixel
                try {
                    const tmpCanvas = document.createElement('canvas');
                    tmpCanvas.width = img.naturalWidth;
                    tmpCanvas.height = img.naturalHeight;
                    const tmpCtx = tmpCanvas.getContext('2d');
                    tmpCtx.drawImage(img, 0, 0);
                    texturePixelData.set(id, tmpCtx.getImageData(0, 0, img.naturalWidth, img.naturalHeight));
                } catch (e) { /* pixel reading won't be available */ }

                console.log(`Sync loaded texture: ${url} (${img.naturalWidth}x${img.naturalHeight})`);
                return { id, width: img.naturalWidth, height: img.naturalHeight };
            } catch (e) {
                console.error('Sync texture load failed for', url, e);
                return null;
            }
        },

        deleteTexture(id) {
            const tex = textures.get(id);
            if (tex) {
                gl.deleteTexture(tex.glTexture);
                textures.delete(id);
                texturePixelData.delete(id);
            }
        },

        getTextureSize(id) {
            const tex = textures.get(id);
            return tex ? { width: tex.width, height: tex.height } : null;
        },

        // Batched sprite drawing
        drawTexture(textureId, srcX, srcY, srcW, srcH, dstX, dstY, dstW, dstH, r, g, b, a, blendMode) {
            setBlendMode(blendMode);

            if (textureId !== batchTextureId) {
                flushBatch();
                batchTextureId = textureId;
            }

            if (batchCount >= MAX_SPRITES) {
                flushBatch();
                batchTextureId = textureId;
            }

            const tex = textures.get(textureId);
            if (!tex) return;

            const tw = tex.width;
            const th = tex.height;
            const u0 = srcX / tw;
            const v0 = srcY / th;
            const u1 = (srcX + srcW) / tw;
            const v1 = (srcY + srcH) / th;

            const cr = r / 255.0;
            const cg = g / 255.0;
            const cb = b / 255.0;
            const ca = a / 255.0;

            const off = batchCount * 4 * 8;
            // Top-left
            batchVertices[off]      = dstX;       batchVertices[off+1]  = dstY;
            batchVertices[off+2]    = u0;         batchVertices[off+3]  = v0;
            batchVertices[off+4]    = cr;         batchVertices[off+5]  = cg;
            batchVertices[off+6]    = cb;         batchVertices[off+7]  = ca;
            // Top-right
            batchVertices[off+8]    = dstX+dstW;  batchVertices[off+9]  = dstY;
            batchVertices[off+10]   = u1;         batchVertices[off+11] = v0;
            batchVertices[off+12]   = cr;         batchVertices[off+13] = cg;
            batchVertices[off+14]   = cb;         batchVertices[off+15] = ca;
            // Bottom-right
            batchVertices[off+16]   = dstX+dstW;  batchVertices[off+17] = dstY+dstH;
            batchVertices[off+18]   = u1;         batchVertices[off+19] = v1;
            batchVertices[off+20]   = cr;         batchVertices[off+21] = cg;
            batchVertices[off+22]   = cb;         batchVertices[off+23] = ca;
            // Bottom-left
            batchVertices[off+24]   = dstX;       batchVertices[off+25] = dstY+dstH;
            batchVertices[off+26]   = u0;         batchVertices[off+27] = v1;
            batchVertices[off+28]   = cr;         batchVertices[off+29] = cg;
            batchVertices[off+30]   = cb;         batchVertices[off+31] = ca;

            batchCount++;
        },

        drawFilledRect(x, y, w, h, r, g, b, a, blendMode) {
            this.drawTexture(whitePixelTexId, 0, 0, 1, 1, x, y, w, h, r, g, b, a, blendMode);
        },

        // Framebuffer (render target) management
        createFramebuffer(width, height) {
            const fbo = gl.createFramebuffer();
            const tex = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, tex);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

            gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);

            const texId = nextTextureId++;
            textures.set(texId, { glTexture: tex, width, height });

            const fbId = nextFbId++;
            framebuffers.set(fbId, { fbo, textureId: texId, width, height });
            return { fbId, textureId: texId };
        },

        bindFramebuffer(fbId) {
            flushBatch();
            if (fbId <= 0) {
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                gl.viewport(0, 0, screenWidth, screenHeight);
                activeFramebuffer = null;
            } else {
                const fb = framebuffers.get(fbId);
                if (fb) {
                    gl.bindFramebuffer(gl.FRAMEBUFFER, fb.fbo);
                    gl.viewport(0, 0, fb.width, fb.height);
                    activeFramebuffer = fb;
                }
            }
        },

        deleteFramebuffer(fbId) {
            const fb = framebuffers.get(fbId);
            if (fb) {
                gl.deleteFramebuffer(fb.fbo);
                this.deleteTexture(fb.textureId);
                framebuffers.delete(fbId);
            }
        },

        // Text rendering via Canvas2D → texture
        measureText(text, fontName, fontSize) {
            if (!textCtx) return { width: 0, height: 0 };
            textCtx.font = `${fontSize}px "${fontName}", sans-serif`;
            const m = textCtx.measureText(text);
            return {
                width: Math.ceil(m.width),
                height: Math.ceil(fontSize * 1.3)
            };
        },

        renderTextToTexture(text, fontName, fontSize, r, g, b, a, borderR, borderG, borderB, borderA) {
            if (!textCtx) return null;
            const font = `${fontSize}px "${fontName}", sans-serif`;
            textCtx.font = font;
            const m = textCtx.measureText(text);
            const w = Math.ceil(m.width) + 4;
            const h = Math.ceil(fontSize * 1.3) + 4;
            if (w <= 0 || h <= 0) return null;

            textCanvas.width = w;
            textCanvas.height = h;
            textCtx.clearRect(0, 0, w, h);
            textCtx.font = font;
            textCtx.textBaseline = 'top';

            // Draw border/outline if present
            if (borderA > 0) {
                textCtx.fillStyle = `rgba(${borderR},${borderG},${borderB},${borderA/255})`;
                for (let ox = -1; ox <= 1; ox++) {
                    for (let oy = -1; oy <= 1; oy++) {
                        if (ox === 0 && oy === 0) continue;
                        textCtx.fillText(text, 2 + ox, 2 + oy);
                    }
                }
            }

            textCtx.fillStyle = `rgba(${r},${g},${b},${a/255})`;
            textCtx.fillText(text, 2, 2);

            const imageData = textCtx.getImageData(0, 0, w, h);
            const texId = this.createTextureFromImage(imageData.data, w, h);
            return { textureId: texId, width: w, height: h };
        },

        // Custom shader support
        createShaderProgram(vertSrc, fragSrc) {
            const prog = createProgramFromSources(vertSrc, fragSrc);
            if (!prog) return -1;
            const id = nextShaderId++;
            shaderPrograms.set(id, prog);
            return id;
        },

        useShaderProgram(id) {
            flushBatch();
            if (id <= 0) {
                gl.useProgram(spriteProgram);
                currentProgram = spriteProgram;
            } else {
                const prog = shaderPrograms.get(id);
                if (prog) {
                    gl.useProgram(prog);
                    currentProgram = prog;
                }
            }
        },

        setUniformFloat(shaderId, name, value) {
            const prog = shaderId > 0 ? shaderPrograms.get(shaderId) : spriteProgram;
            if (prog) {
                const loc = gl.getUniformLocation(prog, name);
                if (loc) gl.uniform1f(loc, value);
            }
        },

        setUniformInt(shaderId, name, value) {
            const prog = shaderId > 0 ? shaderPrograms.get(shaderId) : spriteProgram;
            if (prog) {
                const loc = gl.getUniformLocation(prog, name);
                if (loc) gl.uniform1i(loc, value);
            }
        },

        setUniformVec4(shaderId, name, x, y, z, w) {
            const prog = shaderId > 0 ? shaderPrograms.get(shaderId) : spriteProgram;
            if (prog) {
                const loc = gl.getUniformLocation(prog, name);
                if (loc) gl.uniform4f(loc, x, y, z, w);
            }
        },

        setUniformVec2(shaderId, name, x, y) {
            const prog = shaderId > 0 ? shaderPrograms.get(shaderId) : spriteProgram;
            if (prog) {
                const loc = gl.getUniformLocation(prog, name);
                if (loc) gl.uniform2f(loc, x, y);
            }
        },

        // GPU Buffer management
        createBuffer(target, sizeBytes, isDynamic) {
            const buf = gl.createBuffer();
            gl.bindBuffer(target, buf);
            gl.bufferData(target, sizeBytes, isDynamic ? gl.DYNAMIC_DRAW : gl.STATIC_DRAW);
            const id = nextBufferId++;
            gpuBuffers.set(id, { buffer: buf, target, sizeBytes });
            return id;
        },

        setBufferData(bufferId, data, offset) {
            const b = gpuBuffers.get(bufferId);
            if (b) {
                gl.bindBuffer(b.target, b.buffer);
                gl.bufferSubData(b.target, offset, new Float32Array(data));
            }
        },

        setBufferDataUint16(bufferId, data, offset) {
            const b = gpuBuffers.get(bufferId);
            if (b) {
                gl.bindBuffer(b.target, b.buffer);
                gl.bufferSubData(b.target, offset, new Uint16Array(data));
            }
        },

        setBufferDataUint32(bufferId, data, offset) {
            const b = gpuBuffers.get(bufferId);
            if (b) {
                gl.bindBuffer(b.target, b.buffer);
                gl.bufferSubData(b.target, offset, new Uint32Array(data));
            }
        },

        deleteBuffer(bufferId) {
            const b = gpuBuffers.get(bufferId);
            if (b) {
                gl.deleteBuffer(b.buffer);
                gpuBuffers.delete(bufferId);
            }
        },

        // Draw with custom vertex/index buffers
        // indexType: 0 = UNSIGNED_SHORT (default), 1 = UNSIGNED_INT
        drawBuffers(vertexBufferId, indexBufferId, textureId, indexCount, primitiveType, indexType) {
            flushBatch();
            const vb = gpuBuffers.get(vertexBufferId);
            const ib = gpuBuffers.get(indexBufferId);
            const tex = textures.get(textureId);
            if (!vb || !ib) return;

            if (tex) {
                gl.activeTexture(gl.TEXTURE0);
                gl.bindTexture(gl.TEXTURE_2D, tex.glTexture);
            }

            gl.bindBuffer(gl.ARRAY_BUFFER, vb.buffer);
            gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ib.buffer);

            // Assume same vertex layout as sprites
            gl.enableVertexAttribArray(0);
            gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 32, 0);
            gl.enableVertexAttribArray(1);
            gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 32, 8);
            gl.enableVertexAttribArray(2);
            gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 32, 16);

            let glPrimitive = gl.TRIANGLES;
            if (primitiveType === 1) glPrimitive = gl.TRIANGLE_STRIP;
            else if (primitiveType === 2) glPrimitive = gl.TRIANGLE_FAN;
            else if (primitiveType === 3) glPrimitive = gl.LINES;
            else if (primitiveType === 4) glPrimitive = gl.LINE_STRIP;

            const glIndexType = indexType === 1 ? gl.UNSIGNED_INT : gl.UNSIGNED_SHORT;
            gl.drawElements(glPrimitive, indexCount, glIndexType, 0);
        },

        // Screenshot
        readPixels() {
            flushBatch();
            const pixels = new Uint8Array(screenWidth * screenHeight * 4);
            gl.readPixels(0, 0, screenWidth, screenHeight, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
            return pixels;
        },

        getCanvasSize() {
            // Use display canvas for size since game-canvas may be hidden
            const c = displayCanvas || canvas;
            return { width: c?.clientWidth || 800, height: c?.clientHeight || 600 };
        },

        // Returns a promise that resolves on the next requestAnimationFrame.
        // Used by the C# frame loop to sync with the browser's display cycle.
        // Without this, Task.Delay(1) fires before compositing and the canvas
        // is cleared before the browser ever displays the rendered frame.
        getGLError() {
            return gl.getError();
        },

        waitForNextFrame() {
            return new Promise(resolve => requestAnimationFrame(resolve));
        },

        // Read a single pixel from a texture's cached pixel data
        // (used for skin color initialization via GetPixel)
        readPixel(textureId, x, y) {
            const data = texturePixelData.get(textureId);
            if (!data) return null;
            if (x < 0 || y < 0 || x >= data.width || y >= data.height) return null;
            const offset = (y * data.width + x) * 4;
            return { r: data.data[offset], g: data.data[offset+1], b: data.data[offset+2], a: data.data[offset+3] };
        },

        // Load a web font via CSS FontFace API
        async loadFont(fontName, url) {
            try {
                const font = new FontFace(fontName, `url(${url})`);
                await font.load();
                document.fonts.add(font);
                console.log(`Font loaded: ${fontName} from ${url}`);
                return true;
            } catch (err) {
                console.warn(`Failed to load font '${fontName}' from ${url}:`, err);
                return false;
            }
        },

        // Check if a font is available
        isFontLoaded(fontName) {
            return document.fonts.check(`12px "${fontName}"`);
        }
    };
})();
