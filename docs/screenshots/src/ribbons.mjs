import { glyph, icon } from './ui.mjs';
const sw = c => `<span class="sw" style="background:${c}"></span>`;

export const FILL_SIGN = [
  { h: 'Select', items: [['L', 'select', 'Select', { on: true }], ['M', [['hand', 'Hand'], ['zoom', 'Zoom']]]] },
  { h: 'Add Content', items: [['L', `<span style="font:700 22px 'Liberation Sans';height:30px;margin-bottom:5px;display:grid;place-items:center">Ab</span>`, 'Add<br>Text'], ['L', 'calendar', 'Date'],
    ['M', [['vtext', 'Vertical Text'], ['field', 'Fill Field'], ['checkbox', 'Checkbox']]]] },
  { h: 'Marks', items: [['M', [[glyph('✓', '#4CAF50'), 'Check'], [glyph('✕', '#F44336'), 'Cross'], [glyph('●', '#e6e6e6', 11), 'Dot']]], ['M', [[glyph('—'), 'Line'], [glyph('○'), 'Circle']]]] },
  { h: 'Text', items: [['H', `<div class="col" style="gap:5px;padding-top:3px">
      <div style="display:flex;gap:4px"><div class="combo" style="width:118px">Arial<span>▾</span></div><div class="combo" style="width:36px">12</div></div>
      <div style="display:flex;gap:1px;align-items:center"><span class="sm"><b>B</b></span><span class="sm"><i style="font-family:serif">I</i></span><span class="sm" style="margin-right:6px"><u>U</u></span><span class="sm" style="font-size:10px">A</span><span class="sm" style="font-size:15px">A</span></div>
      <div style="display:flex;gap:3px">${['#000', '#1A237E', '#0050C8', '#B71C1C', '#1B5E20', '#757575', '#fff'].map(sw).join('')}</div></div>`]] },
  { h: 'Sign', items: [['L', 'sign', 'Sign'], ['M', [['pen', 'New Signature…'], ['initials', 'New Initials…'], ['stamp', 'Stamp']]],
    ['H', `<div class="col" style="gap:4px;padding-top:2px"><div class="combo" style="width:150px"><span><span class="dot" style="background:#1B7A2E"></span>&nbsp; APPROVED</span><span>▾</span></div><div class="md">${icon('plus')}Custom Stamp…</div></div>`]] },
  { h: 'Fields', items: [['L', 'hlfields', 'Highlight<br>Fields'], ['M', [['clear', 'Clear All'], ['trash', 'Delete Field', { dis: true }]]]] },
  { h: 'Comment', items: [['M', [['note', 'Sticky Note'], ['highlight', 'Highlight'], ['draw', 'Draw']]], ['M', [['eraser', 'Eraser'], ['link', 'Link']]]] },
  { h: 'Finish', items: [['L', 'save', 'Save'], ['M', [['flatten', 'Flatten &amp; Save'], ['print', 'Print'], ['undo', 'Undo']]]] },
];

export const HOME = [
  { h: 'File', items: [['L', 'open', 'Open'], ['L', 'scan', 'Scan'], ['L', 'save', 'Save'], ['M', [['saveas', 'Save As'], ['print', 'Print'], ['close', 'Close']]], ['M', [['settings', 'Settings']]]] },
  { h: 'Clipboard', items: [['L', 'paste', 'Paste'], ['M', [['cut', 'Cut'], ['copy', 'Copy']]]] },
  { h: 'Navigation', items: [['M', [['first', 'First'], ['prev', 'Previous'], ['next', 'Next']]], ['M', [['last', 'Last']]]] },
  { h: 'Search', items: [['L', 'search', 'Search']] },
  { h: 'Pages', items: [['M', [['pagedel', 'Delete Page'], ['pageadd', 'Insert Before'], ['copy', 'Duplicate Page']]], ['M', [['pageadd', 'Insert After'], ['up', 'Move Up'], ['down', 'Move Down']]],
    ['L', 'split', 'Split<br>PDF'], ['L', 'merge', 'Merge<br>PDFs'], ['L', 'compare', 'Compare<br>PDFs'], ['L', 'watermark', 'Watermark'], ['M', [['close', 'Remove Watermark'], ['thumbs', 'Thumbnails']]], ['L', 'numbers', 'Page<br>Numbers'], ['L', 'compress', 'Compress']] },
  { h: 'Document', items: [['L', 'info', 'Properties'], ['L', 'header', 'Header /<br>Footer'], ['L', 'crop', 'Crop<br>Pages']] },
  { h: 'Security', items: [['L', 'lock', 'Password<br>Protect'], ['L', 'unlock', 'Remove<br>Password']] },
];

export const TOOLS = [
  { h: 'Active Tool', items: [['L', 'hand', 'Hand'], ['L', 'select', 'Select', { on: true }], ['L', 'field', 'Text Fill'], ['L', 'highlight', 'Highlight'], ['M', [['underline', 'Underline'], ['strike', 'Strikethrough'], ['redact', 'Redact']]], ['L', 'apply', 'Apply<br>Redactions']] },
  { h: 'Drawing', items: [['L', 'draw', 'Freehand'], ['L', 'link', 'Link'], ['L', 'note', 'Sticky<br>Note'], ['M', [['insert', 'Insert Text'], ['replace', 'Replace Text'], ['comments', 'Comments']]],
    ['M', [['rect', 'Rectangle'], ['ellipse', 'Ellipse'], ['arrow', 'Arrow']]], ['M', [['callout', 'Callout'], ['line', 'Line'], ['cloud', 'Cloud']]], ['M', [['polygon', 'Polygon'], ['polyline', 'Polyline'], ['eraser', 'Eraser']]]] },
  { h: 'Measure', items: [['L', 'ruler', 'Distance'], ['M', [['perimeter', 'Perimeter'], ['area', 'Area']]]] },
  { h: 'Sign &amp; Date', items: [['L', 'sign', 'Signature'], ['L', 'calendar', 'Date<br>Stamp'], ['L', 'stamp', 'Stamp'],
    ['H', `<div class="col" style="gap:4px;padding-top:2px"><div class="combo" style="width:150px"><span><span class="dot" style="background:#C62828"></span>&nbsp; SIGN HERE</span><span>▾</span></div><div class="md">${icon('plus')}Custom Stamp</div><div class="md">${icon('trash')}Remove Custom</div></div>`]] },
  { h: 'Marks', items: [['M', [[glyph('✓', '#4CAF50'), 'Check'], [glyph('✕', '#F44336'), 'Cross'], [glyph('●', '#e6e6e6', 11), 'Dot']]]] },
];

export const EDIT = [
  { h: 'History', items: [['L', 'undo', 'Undo'], ['L', 'redo', 'Redo']] },
  { h: 'Form Data', items: [['M', [['importi', 'Import Data'], ['json', 'Import from JSON'], ['exporti', 'Export Data']]], ['M', [['flatten', 'Flatten &amp; Save']]]] },
  { h: 'Tools', items: [['L', 'findrep', 'Find &amp;<br>Replace'], ['L', 'valid', 'Validate<br>Required'], ['L', 'archive', 'Archive<br>(PDF/A)'], ['L', 'textbox', 'Export<br>Text']] },
  { h: 'Add Form Field', items: [['L', 'edit', 'Edit<br>Fields', { on: true }], ['L', 'field', 'Text<br>Field'], ['L', 'checkbox', 'Checkbox'], ['L', 'combo', 'Combo<br>Box'], ['L', 'radio', 'Radio<br>Button']] },
  { h: 'Arrange Fields', items: [['M', [['alignl', 'Lefts'], ['alignc', 'Centres'], ['alignr', 'Rights']]], ['M', [['alignt', 'Tops'], ['alignm', 'Middles'], ['alignb', 'Bottoms']]],
    ['M', [['spacex', 'Space Across'], ['spacey', 'Space Down'], ['samew', 'Same Width']]], ['M', [['sameh', 'Same Height'], ['samesz', 'Same Size'], ['centerp', 'Center Page']]]] },
];
