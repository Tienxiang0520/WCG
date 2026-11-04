// Adds the analytics script tag to the Azure build's index.html.
export const analyticsTag='<script src="analytics.js" async></script>';
export function addAnalytics(html){
    if(html.includes(analyticsTag))return html;
    if(!html.includes('</head>'))throw new Error('index.html has no </head>.');
    return html.replace('</head>',`    ${analyticsTag}\n</head>`);
}
